# AWS DDNS on Route 53

## Vision

Explore a simple dynamic DNS system that keeps Route 53 records aligned with the current public IP addresses of participating systems.

The purpose of the project is not to build a real-time service discovery system or a production-grade DDNS platform. It is to create a paper prototype that demonstrates a reliable update flow and explores the practical considerations involved in using Route 53 as the destination for dynamic address information.

The desired property is straightforward:

> when a participating system's public IP changes, its DNS record should eventually and reliably reflect the new address.

Reasonable convergence is more important than immediate convergence.

## Problem

Some systems operate from networks where their externally visible IP address can change.

Those systems may still need a stable name that other systems can use to find them.

DNS provides the natural interface:

> stable hostname → current public IP address

The difficulty is maintaining that relationship when the address can change without notice.

A basic DDNS system needs to determine:

- which system is reporting;
- what its current public IP address is;
- which DNS record represents it;
- whether the existing DNS state is already correct;
- and whether Route 53 needs to be updated.

The interesting part of the experiment is making this reliable without turning every observation of an IP address into an unnecessary Route 53 API operation.

## Basic Flow

A participating system periodically reports its current public IP address.

That information is received by a service responsible for determining the desired DNS state.

Conceptually:

> client → report current address → DDNS service → Route 53 → DNS

The exact implementation of the client and service is intentionally open.

For example, the client could determine its own externally visible address or the receiving service could infer it from the connection.

The purpose of the project is to explore the overall architecture rather than prescribe the transport mechanism in advance.

## Desired State

Think of the service primarily as maintaining desired DNS state.

For a particular hostname:

> desired address = latest accepted address for that system

Route 53 represents the currently applied state.

The system's responsibility is to make those states converge.

This framing allows client reporting and DNS mutation to be separated.

A client reporting its address does not necessarily need to result immediately in a Route 53 API request.

Instead:

> observe → record desired state → reconcile → update when necessary

This separation should be explored because it creates opportunities for deduplication, batching, retry, and controlled API consumption.

## Reliability Over Immediacy

Real-time updates are not a requirement.

If a public IP changes, a short delay before DNS converges is acceptable.

The more important properties are:

- changes are eventually detected;
- changes are eventually applied;
- repeated reports are harmless;
- temporary AWS failures do not permanently lose an update;
- unnecessary Route 53 changes are avoided;
- and the system eventually returns to the correct state after interruptions.

The prototype should therefore favour simple eventual consistency over elaborate attempts to minimize every second of propagation delay.

## Route 53 API Behaviour

Route 53 is an external service with API limits and its own change-processing behaviour.

The design should account for this rather than treating DNS changes as unlimited individual writes.

Explore approaches that avoid creating a direct relationship between:

> number of client reports

and:

> number of Route 53 change requests

If one hundred clients repeatedly report unchanged addresses, the system should ideally perform little or no DNS work.

Areas worth exploring include:

- deduplicating identical reports;
- updating only when desired state differs from known DNS state;
- grouping compatible record changes;
- controlling reconciliation frequency;
- retrying transient failures;
- applying backoff when appropriate;
- and avoiding unnecessary Route 53 lookups or updates.

Do not optimize prematurely around a particular numeric API limit. The more important goal is to develop an architecture that behaves reasonably under finite API capacity.

## Reconciliation

A reconciliation model is likely worth testing.

Conceptually:

1. accept address observations;
2. store the latest desired state;
3. identify records whose desired state differs from their applied state;
4. submit the required Route 53 changes;
5. confirm or observe the resulting state;
6. retry failures where appropriate.

This allows reporting frequency and DNS update frequency to differ.

For example, clients might report relatively often while DNS changes occur only when an actual address change has been observed.

The exact reconciliation mechanism can be chosen during implementation.

The project should determine how much machinery is actually necessary rather than assuming a complex controller architecture from the beginning.

## DNS Behaviour

The experiment should also account for the fact that updating Route 53 does not make every DNS consumer immediately observe the new address.

DNS caching and record TTLs remain part of the behaviour.

Explore a reasonable balance between:

- how quickly Route 53 is updated;
- the TTL of dynamic records;
- the resulting DNS query volume;
- and how quickly consumers need to observe an address change.

The project does not need to optimize this perfectly.

It should simply demonstrate an understanding that:

> successful Route 53 change ≠ instantaneous global DNS change

## Identity and Record Mapping

The service needs some way to associate a reporting system with the DNS state it is allowed to influence.

The first prototype can keep this deliberately simple.

The useful conceptual mapping is:

> system identity → DNS record → current address

How that identity is represented is open to experimentation.

For example, the association could come from configuration, an identifier supplied by the client, generated registration information, or another simple mechanism.

Avoid spending the majority of the initial experiment designing a sophisticated identity platform.

The important question is whether the DDNS flow itself works reliably.

## Questions

The project should help answer questions such as:

- What is the simplest useful client reporting mechanism?
- Should the client report its address or should the service infer it?
- Should desired state be persisted separately from Route 53?
- How frequently should clients report?
- How frequently should reconciliation occur?
- How much batching is useful?
- How should repeated identical observations be handled?
- How should failed Route 53 changes be retried?
- What happens if several address changes occur before reconciliation?
- How should stale clients be represented?
- How should DNS TTL relate to expected address-change frequency?
- What Route 53 behaviour materially affects the architecture?
- How many clients can a simple implementation support before the design needs to change?
- Is a dedicated reconciliation process useful, or is direct conditional updating sufficient?

These are questions for the prototype to explore rather than requirements for a predetermined architecture.

## Boundaries

Keep the first implementation focused on the dynamic DNS flow.

Do not initially attempt to solve every production concern around:

- sophisticated authentication;
- authorization systems;
- abuse prevention;
- high availability;
- multi-region deployment;
- elaborate user interfaces;
- comprehensive monitoring;
- or large-scale tenancy.

Basic security sufficient to make the experiment sensible is appropriate, but security architecture should not obscure the primary question being investigated.

Likewise, this project does not need to become a general-purpose DNS management platform.

The focus is specifically on keeping a known set of Route 53 records aligned with changing addresses.

## Expected Output

The repository should contain a working demonstration of the complete DDNS cycle.

Useful outputs may include:

- a simple reporting client;
- a mechanism for receiving address observations;
- representation of desired address state;
- Route 53 reconciliation or update logic;
- handling for unchanged addresses;
- basic retry behaviour;
- examples involving several independently changing hosts;
- and observations about API usage and DNS convergence.

Where multiple implementation approaches appear reasonable, small comparative experiments are preferable to prematurely choosing a generalized architecture.

## Success

The work is successful if a participating system can change public IP addresses and the corresponding Route 53 record reliably converges on the new value without requiring manual intervention.

The implementation should also demonstrate that frequent client reporting does not necessarily imply frequent Route 53 mutation.

The desired result is a simple and understandable answer to:

> What is the smallest reasonably reliable system for using Route 53 as dynamic DNS for a collection of changing endpoints?

The prototype should provide enough experience with that architecture to determine whether it is worth developing further and what a more durable implementation would need.
