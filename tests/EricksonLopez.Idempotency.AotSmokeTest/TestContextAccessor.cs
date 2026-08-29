// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.AotSmokeTest;

public sealed class TestContextAccessor : IIdempotencyContextAccessor
{
    public IdempotencyContext? IdempotencyContext { get; set; }
}
