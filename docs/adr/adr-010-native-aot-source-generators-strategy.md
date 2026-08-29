# ADR-010: Native AOT & System.Text.Json Source Generator Strategy

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
Reflection-heavy serializers break trimming and Native AOT compilation, causing runtime exceptions when deployed as standalone AOT binaries.

## Decision
Enforce `IsAotCompatible=true` and `EnableTrimAnalyzer=true` across all projects. Use `System.Text.Json` with source-generated `IdempotencyJsonContext` for all internal DTOs and problem details.

## Consequences
- **Positive**: Full Native AOT compatibility, zero trimming warnings, instantaneous application startup, and reduced memory footprints.
- **Negative**: Consumer types serialized through generic contracts must be annotated with `[JsonSerializable]` in their own applications for Native AOT.
