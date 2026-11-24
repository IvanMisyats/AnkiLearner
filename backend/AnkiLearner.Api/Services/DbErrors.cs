using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AnkiLearner.Api.Services;

public static class DbErrors
{
    /// <summary>A save rejected by a unique index (Postgres 23505), e.g. two requests inserting
    /// the same card's first state at once.</summary>
    public static bool IsUniqueViolation(Exception e) =>
        e is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };
}
