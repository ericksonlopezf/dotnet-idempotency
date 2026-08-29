# ADR-009: Mediator Pipeline Integration via IPipelineBehavior

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
Application layers using `EricksonLopez.Mediator` require seamless command interception based on strongly-typed marker interfaces without modifying command handlers.

## Decision
Provide `IdempotencyPipelineBehavior<TRequest, TResponse>` targeting `IIdempotentRequest` within an optional package `EricksonLopez.Idempotency.Mediator`.

## Consequences
- **Positive**: Zero coupling in `EricksonLopez.Idempotency.Core` to Mediator or MediatR.
- **Positive**: Handler logic remains purely focused on domain use-case execution.
