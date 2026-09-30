using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Shelf.Core.Data;

public static class SqlErrors
{
    /// <summary>
    /// True when SQL Server rejected the write because the key already exists:
    /// 2627 is a primary key or unique constraint violation, 2601 a unique index violation.
    /// </summary>
    public static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2627 || sql.Number == 2601);
}
