# ADR-010: Result Pattern for Application Layer Error Handling

**Date:** 2026-03-19
**Status:** Accepted

---

## Context

Application layer handlers orchestrate use cases that can fail in several distinct ways:

- A requested resource does not exist (`NotFound`)
- A duplicate upload with the same idempotency key is submitted (`Conflict`)
- A requeue is attempted on a job in a non-requeue-able state (`Conflict`)
- A domain invariant is violated because the handler constructed an invalid operation (`Unexpected`)

Two approaches were considered for communicating these outcomes to callers:

1. **Exceptions** — handlers throw typed exceptions (e.g. `NotFoundException`, `ConflictException`) which propagate up the call stack to a global middleware that maps them to HTTP responses.
2. **Result type** — handlers return a `Result<T>` value that is either a success (carrying the value) or a failure (carrying a structured error). Callers are forced to inspect the result before proceeding.

---

## Decision

All Application layer handlers return `Result<T>`. Handlers never throw for expected business outcomes.

```csharp
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public ApplicationError? Error { get; }

    public static Result<T> Success(T value)
    public static Result<T> Conflict(string code, string message)
    public static Result<T> NotFound(string code, string message)
    public static Result<T> Validation(string code, string message)
}

public sealed record ApplicationError(string Code, string Message, ErrorType Type);

public enum ErrorType { Validation, Conflict, NotFound, Unexpected }
```

The API layer maps `ErrorType` to HTTP status codes in a single, centralised location:

```csharp
private static IResult MapError(ApplicationError error) => error.Type switch
{
    ErrorType.NotFound  => Results.Problem(statusCode: 404, ...),
    ErrorType.Conflict  => Results.Problem(statusCode: 409, ...),
    _                   => Results.Problem(statusCode: 500, ...)
};
```

### Relationship to DomainException

The Domain layer uses `DomainException` to protect invariants — for example, rejecting an invalid status transition. These exceptions signal that the **handler made a programming error** (called an operation in the wrong order, passed invalid state). They are not expected business outcomes.

Handlers catch `DomainException` at their boundary and translate it to an appropriate `Result` only when the violation is a foreseeable business condition (e.g. a requeue attempt on a succeeded job). Violations that indicate a true programming error are allowed to propagate as unhandled exceptions, which surface as 500 responses.

```
Domain throws DomainException     → invariant violated (programming error)
Handler returns Result.Conflict   → expected business outcome (requeue not allowed)
Handler returns Result.NotFound   → expected business outcome (job does not exist)
```

---

## Consequences

### Benefits

- **Explicit at every callsite** — `Result<T>` is a value, not a side-effect. Every caller must branch on `IsSuccess` before accessing `Value`. There is no way to accidentally ignore an error the way a swallowed exception can be.

- **Callers are decoupled from exception types** — the API and Worker layers depend on `ApplicationError` and `ErrorType`, not on `DomainException`, `NpgsqlException`, or any other internal type. Changing how a handler detects an error requires no changes to callers.

- **HTTP mapping is centralised and consistent** — every endpoint uses the same `MapError` method. Adding a new `ErrorType` requires one addition in `MapError`; it does not require touching every handler. The mapping between application errors and HTTP semantics is visible in one place.

- **Unexpected exceptions remain visible** — only expected business outcomes are expressed as `Result`. Unhandled exceptions (infrastructure failures, programming errors) still propagate normally and are caught by ASP.NET Core's exception middleware, which returns 500. This distinction is intentional: a `NotFound` is a normal outcome; an `NpgsqlException` from a broken connection is not.

### Trade-offs

- **More verbose than throw/catch** — every callsite must check `result.IsFailure` and return or handle the error. In deep call chains this can produce repetitive `if (result.IsFailure) return result` boilerplate. A monadic `Bind` / `Map` operator would reduce this but adds cognitive overhead for developers unfamiliar with the pattern.

- **No stack trace on business errors** — `Result.NotFound(...)` carries a code and message but no stack trace. This is intentional — business outcomes are not exceptional — but it means that if a `NotFound` appears unexpectedly during debugging, there is no automatic trace to its origin.

- **`Result<T>` is not standardised in .NET** — unlike languages with built-in result types (Rust's `Result<T, E>`, F#'s `Result<'T, 'E>`), C# has no canonical implementation. The `Result<T>` class in this project is custom. Developers joining the team must learn the local convention.

### When to revisit

- If handler chaining becomes deeply nested, introduce `Result.Bind(func)` / `Result.Map(func)` extension methods to flatten the call chain without changing the core contract.
- If the number of `ErrorType` values grows significantly, consider splitting into domain-specific error subtypes rather than a single enum.
