# Deploying to Azure Container Apps + Azure Database for PostgreSQL

This guide sets up Signage with the Azure CLI: an Azure Database for PostgreSQL flexible server, an Azure Container
Registry for the image, and a Container Apps environment that runs the app. It is a how-to, not infrastructure as code;
adapt names, sizes and regions. Check the Azure documentation for options that changed since this was written.

**Before you start:** Signage is meant for a trusted network (see the README's security section). In the cloud, keep it
off the open internet: use an internal environment reached over VPN/ExpressRoute, or at least restrict ingress to your
office's addresses (step 6).

## 1. Variables

```bash
RG=signage-rg
LOCATION=westeurope
PG=signage-pg-$RANDOM            # server names are global
ACR=signageacr$RANDOM            # registry names are global, letters and digits only
ENV=signage-env
APP=signage
PG_PASSWORD='<a long random password>'
ADMIN_EMAIL='you@example.org'
ADMIN_PASSWORD='<a temporary password, at least 10 characters>'

az group create -n $RG -l $LOCATION
```

## 2. PostgreSQL

```bash
az postgres flexible-server create -g $RG -n $PG -l $LOCATION \
  --version 16 --tier Burstable --sku-name Standard_B1ms --storage-size 32 \
  --admin-user signage --admin-password "$PG_PASSWORD" \
  --public-access 0.0.0.0          # "allow Azure services"; prefer VNet integration for production
az postgres flexible-server db create -g $RG -s $PG -d signage
```

Notes:

- Connect on port **5432**, not the built-in PgBouncer port (6432). Live updates use PostgreSQL `LISTEN/NOTIFY`, which
  needs a real session; transaction pooling breaks it.
- The server requires TLS. Its certificate chains to a public root CA that the container trusts, so
  `SSL Mode=VerifyFull` works.
- Burstable B1ms is plenty for dozens of screens. Each app replica keeps a handful of connections plus one for
  `LISTEN`.

## 3. The image

Build the image locally with Docker Buildx (the Dockerfile needs BuildKit) and push it to a new registry:

```bash
az acr create -g $RG -n $ACR --sku Basic
az acr login -n $ACR
docker buildx build --platform linux/amd64 -t $ACR.azurecr.io/signage:0.1.0 --push .   # in the repository root
```

Container Apps run amd64. (`az acr build` isn't used here: it isn't shown to build with BuildKit, which this Dockerfile
requires.)

## 4. The Container Apps environment

```bash
az containerapp env create -g $RG -n $ENV -l $LOCATION
# Internal only (reachable from a VNet you connect to): add --internal-only true with --infrastructure-subnet-resource-id.
```

## 5. The app

```bash
CONN="Host=$PG.postgres.database.azure.com;Port=5432;Database=signage;Username=signage;Password=$PG_PASSWORD;SSL Mode=VerifyFull"

az containerapp create -g $RG -n $APP --environment $ENV \
  --image $ACR.azurecr.io/signage:0.1.0 --registry-server $ACR.azurecr.io --registry-identity system \
  --ingress external --target-port 8080 \
  --cpu 0.5 --memory 1Gi --min-replicas 1 --max-replicas 2 \
  --secrets db="$CONN" admin-password="$ADMIN_PASSWORD" \
  --env-vars ConnectionStrings__Default=secretref:db \
             SIGNAGE__BOOTSTRAP__ADMINEMAIL="$ADMIN_EMAIL" \
             SIGNAGE__BOOTSTRAP__ADMINPASSWORD=secretref:admin-password \
             SIGNAGE__FORWARDEDHEADERS__ENABLED=true
```

- **`--min-replicas 1`:** screens hold a live connection to the app. Scaling to zero would cut them off (they keep
  playing, but changes would only arrive after a restart). Two or more replicas are fine: events reach every screen
  whichever replica it is connected to.
- **Migrations** run at start-up; replicas starting together take turns.
- **The first admin** is created from the bootstrap variables when the database has no users, and must change the
  password at first sign-in. You can remove the two variables afterwards.
- **Forwarded headers:** the Container Apps ingress terminates HTTPS and sets `X-Forwarded-For`/`-Proto`. The app is
  only reachable through the ingress, so `KNOWNNETWORKS` can stay empty. The admin then sees each screen's real
  address (R-30) and the sign-in cookie is marked `Secure`.
- **Live updates** use server-sent events. The ingress keeps idle streams open for several minutes, and the app sends a
  keep-alive every 15 s, so nothing needs tuning.

Get the address:

```bash
az containerapp show -g $RG -n $APP --query properties.configuration.ingress.fqdn -o tsv
```

Open `https://<fqdn>/admin`, sign in, and point screens at `https://<fqdn>/screen`.

## 6. Restrict who can reach it

Allow only your office's public address range(s) at the ingress:

```bash
az containerapp ingress access-restriction set -g $RG -n $APP \
  --rule-name office --ip-address 203.0.113.0/24 --action Allow
```

With an allow rule in place, everything else is denied. Screens and admins must come from the allowed ranges.

## 7. Health probes (optional)

The container has no shell, so probes must be HTTP. In the app's YAML (`az containerapp show -o yaml`, edit, then
`az containerapp update --yaml`), set a liveness probe on `/healthz` and a readiness probe on `/readyz`, both on port
8080.

## Updating

Build a new tag and point the app at it:

```bash
docker buildx build --platform linux/amd64 -t $ACR.azurecr.io/signage:0.2.0 --push .
az containerapp update -g $RG -n $APP --image $ACR.azurecr.io/signage:0.2.0
```

Migrations run when the new revision starts, and the screens reload themselves when they see the new version.
