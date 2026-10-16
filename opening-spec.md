# Basic Digital Signage system

In an office space, there are several TV's mounted in different departments, in common areas, the grand hall etc.

These should be administered from a single backend/admin system.

The monitors are able to show standard web pages.  Could be built-in browser or an attached mini computer as Arduino or Rasperry Pi.

The entire system is running on a website running in a container - deployable to most popular services. AWS, Azure, K8s (hosted or local) or simply on Docker desktop or alike.

Review the terms "Monitor" and "Cards". Could be something else that makes more sense.

Thoughts on possible platforms - .NET, TypeScript/Node.js, Go - Lightweight HMTL/CSS/Javascript - Postgres

AI development sessions are allowed to install these specific SDKs on the development machine.  .NET 10 SDK and Docker is already installed.

## Milestone 1

The monitor displays *cards* in a carousel.  A card can be a full frame image, a full frame MP4 video, an IFRAME or an HTML chunk. In Milestone 1, images and videos are always URLs to content hosted elsewhere.

There's a global standard setting for duration of a card.  It should be possible to override the duration setting for each card.

Transition in the carousel should be a nice, firm ease-in coming from the right to the left.  A subtle shadow on the left side of the new card coming in.  It appears stacked, so the previous card stays in position under the new card.

Very subtle progress bar at the bottom of the screen indicates when the card changes to the next one.

When a card is a video, the video starts when the card is fully in view. When the video is done, it stops and a new card slides over.
Performance wise, I could imagine that the video element needs to be removed from DOM when a new card has been fully placed on top.  But investigate this first.

There should also be a "single video mode", where a monitor displays just one video in loop.

Monitors internally in departments might display semi internal information, where a monitor eg in a grand hall should display more general or customer oriented information.  It would be nice to be able to "group" monitors in some way or another.

A monitor should be able to contain zero or more "standard" feeds and have something else manually appended or prepended.  Could be that the canteen has one feed and Marketing has another one etc.

User admin - At least two levels of users. Admin users and editors. Only admins can give access to new users.

Sometimes it can be hard to identify a specific monitor.  It should be possible to name each of them.
And possible to set the entire system into a "display monitor name and IP address subtly at the bottom of the screen" mode. Maybe also some brilliant way to identify one specific monitor.  Maybe a thick red border for a limited time.


### Security in Milestone 1
We expect the users to have set up proper firewalls to block unwanted traffic to the signage system.


## Probable future additions
* Standard scripts/configs for Rasperry Pi or other mini computers
* SSO via M365 (and others?)
* Users with access to specific groups of monitors


## Other
The code should end up as something that could be put in a public Github repository available for whoever wants to clone it.  Without warranty or liability.

There should be at least one reviewer session acting as a senior/principal software engineer not sharing memory with the developer session.