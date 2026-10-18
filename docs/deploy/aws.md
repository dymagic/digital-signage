# Deploying to AWS: ECS Fargate (or App Runner) + Amazon RDS for PostgreSQL

The recommended setup is **ECS on Fargate behind an Application Load Balancer**, with **RDS for PostgreSQL**. App Runner
also works, with limits listed at the end. This is a how-to, not infrastructure as code: the steps name what to create
and the settings that matter for Signage. Check the AWS documentation for the console or CLI details current at the
time.

**Before you start:** Signage is meant for a trusted network (see the README's security section). Keep it off the open
internet: an internal load balancer reached over VPN/Direct Connect, or a load balancer whose security group admits only
your office's address ranges.

## 1. Network

One VPC with two private subnets (tasks and database) and two public subnets (an internet-facing load balancer), or
private subnets only for an internal load balancer. Three security groups:

| Group | Inbound |
|---|---|
| `signage-alb` | 443 (and 80, if you redirect) from your office ranges or your VPN |
| `signage-app` | 8080 from `signage-alb` |
| `signage-db` | 5432 from `signage-app` |

Tasks need outbound access to ECR (a NAT gateway or VPC endpoints), and screens need to reach whatever hosts your image,
video and web-page slides point at.

## 2. RDS for PostgreSQL

Create a PostgreSQL 16 instance (for example `db.t4g.micro` with 20 GB gp3) in the private subnets with `signage-db`,
master user `signage`, and an initial database `signage`.

- Connect the app **directly**, not through **RDS Proxy**: live updates use PostgreSQL `LISTEN/NOTIFY`, which needs a
  session of its own, and a proxy pins or breaks it.
- Use `SSL Mode=Require` in the connection string. (For `VerifyFull`, the RDS certificate authority bundle would have to
  be added to the container's trust store, which the stock image doesn't do.)

## 3. The image

Create an ECR repository `signage` and push a build:

```bash
aws ecr get-login-password --region eu-west-1 | docker login --username AWS --password-stdin <account>.dkr.ecr.eu-west-1.amazonaws.com
docker buildx build --platform linux/arm64 -t <account>.dkr.ecr.eu-west-1.amazonaws.com/signage:0.1.0 --push .
```

`linux/arm64` runs on Graviton Fargate tasks (cheaper); build `linux/amd64` (or both) if you use x86 tasks.

## 4. Secrets

In Secrets Manager (or SSM Parameter Store), store:

- `signage/db`: `Host=<rds endpoint>;Port=5432;Database=signage;Username=signage;Password=<password>;SSL Mode=Require`
- `signage/admin-password`: a temporary password for the first admin (at least 12 characters)

Give the ECS **task execution role** permission to read them.

## 5. ECS service

Create an ECS cluster and a task definition (Fargate, 0.5 vCPU and 1 GB is plenty) with one container:

```json
{
  "name": "signage",
  "image": "<account>.dkr.ecr.eu-west-1.amazonaws.com/signage:0.1.0",
  "portMappings": [{ "containerPort": 8080, "protocol": "tcp" }],
  "environment": [
    { "name": "SIGNAGE__BOOTSTRAP__ADMINEMAIL", "value": "you@example.org" },
    { "name": "SIGNAGE__FORWARDEDHEADERS__ENABLED", "value": "true" },
    { "name": "SIGNAGE__FORWARDEDHEADERS__KNOWNNETWORKS", "value": "10.0.0.0/16" }
  ],
  "secrets": [
    { "name": "ConnectionStrings__Default", "valueFrom": "<arn of signage/db>" },
    { "name": "SIGNAGE__BOOTSTRAP__ADMINPASSWORD", "valueFrom": "<arn of signage/admin-password>" }
  ],
  "runtimePlatform": { "cpuArchitecture": "ARM64", "operatingSystemFamily": "LINUX" }
}
```

(`runtimePlatform` belongs to the task definition, next to `containerDefinitions`.) Set `KNOWNNETWORKS` to your VPC's
CIDR, where the load balancer lives.

Create a service with **1 or more** tasks (don't scale to zero: screens keep a live connection), in the private subnets
with `signage-app`, attached to the load balancer below. Several tasks are fine: events reach every screen whichever
task it is connected to, and migrations at start-up take turns. No sticky sessions are needed.

## 6. Application Load Balancer

- A target group, type **IP**, protocol HTTP, port 8080, health check path **`/healthz`** (the container has no shell,
  so there is no container health check; `/readyz` additionally checks the database).
- An HTTPS listener (443) with an ACM certificate, forwarding to the target group; optionally port 80 redirecting to 443.
- The default idle timeout (60 s) is fine: the app sends a keep-alive on its event streams every 15 s.

Open `https://<your name for the load balancer>/admin`, sign in with the bootstrap account (you'll be asked to change the
password), and point screens at `https://<name>/screen`. You can remove the bootstrap variables afterwards.

## Updating

Push a new tag, register a new task definition revision with it, and update the service. Migrations run when the new
tasks start, and screens reload themselves when they see the new version.

## App Runner

App Runner can run the same image (port 8080, the same environment variables and secrets, a VPC connector to reach RDS).
Two things to know:

- Long-lived requests are cut after App Runner's request timeout, so screens reconnect their event stream every few
  minutes. That works (the player reconnects by itself and polls every 60 s), but ECS with an ALB is smoother.
- Restricting access to your office means a private App Runner service (a VPC interface endpoint) or a web ACL;
  an ALB security group is simpler.
