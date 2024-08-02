using Microsoft.Data.Sqlite;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;

namespace CypherPotato.SqliteCollections;

/// <summary>
/// Provides an Sqlite based data-persistant list of strings.
/// </summary>
public sealed class SqliteList : IDisposable, IList<string?>
{
    private SqliteConnection connection;
    private bool disposedValue;

    /// <summary>
    /// Opens an new read-only <see cref="SqliteList"/> instance in the specified
    /// database name.
    /// </summary>
    /// <param name="databaseName">The database name (connection string).</param>
    public static SqliteList OpenRead(string databaseName)
    {
        return new SqliteList(databaseName, true);
    }

    /// <summary>
    /// Opens an new <see cref="SqliteList"/> instance in the specified
    /// database name.
    /// </summary>
    /// <param name="databaseName">The database name (connection string).</param>
    public static SqliteList Open(string databaseName)
    {
        return new SqliteList(databaseName, false);
    }

    /// <summary>
    /// Gets or sets an value based on their index.
    /// </summary>
    /// <param name="index">The zero-based object index.</param>
    public string? this[int index]
    {
        get
        {
            CheckDisposed();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT value FROM list WHERE rowid = @index;
                    """;

                command.Parameters.AddWithValue("index", index + 1);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        if (reader.IsDBNull(0))
                        {
                            return null;
                        }
                        return reader.GetString(0);
                    }
                }
            }

            return null;
        }
        set
        {
            CheckDisposed();
            CheckReadonly();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT OR REPLACE INTO list (rowid, value) VALUES (@index, @value);
                    """;

                command.Parameters.AddWithValue("index", index + 1);
                command.Parameters.AddWithValue("value", value);
                command.ExecuteNonQuery();
            }
        }
    }

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            CheckDisposed();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT COUNT(value) FROM list;
                    """;

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return reader.GetInt32(0);
                    }
                }
            }
            return 0;
        }
    }

    /// <summary>
    /// Gets an boolean indicating if this <see cref="SqliteList"/> is read-only.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    internal SqliteList(string databaseName, bool isReadOnly = true)
    {
        this.IsReadOnly = isReadOnly;

        if (!databaseName.EndsWith(".db", StringComparison.CurrentCultureIgnoreCase))
            databaseName += ".db";

        connection = new SqliteConnection($"Data Source={databaseName};");
        connection.Open();

        EnsureListTable();
    }

    void EnsureListTable()
    {
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS "list" (
                	"value"	TEXT
                );
                """;

            command.ExecuteNonQuery();
        }
    }

    void CheckDisposed()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SqliteList));
    }

    void CheckReadonly()
    {
        if (IsReadOnly) throw new InvalidOperationException("Cannot modify this dictionary: this database was openned in read-only mode.");
    }

    /// <inheritdoc/>
    public void Add(string? item)
    {
        CheckDisposed();
        CheckReadonly();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO list (value) VALUES (@value);
                """;

            command.Parameters.AddWithValue("value", item);
            command.ExecuteNonQuery();
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        CheckDisposed();
        CheckReadonly();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM list;
                """;
            command.ExecuteNonQuery();
        }
    }

    /// <inheritdoc/>
    public bool Contains(string? item)
    {
        CheckDisposed();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT * FROM list WHERE value = @value LIMIT 1;
                """;

            command.Parameters.AddWithValue("value", item);

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <inheritdoc/>
    [Obsolete("This database does not support this action.")]
    public void CopyTo(string?[] array, int arrayIndex)
    {
        throw new NotSupportedException("This database does not support this action.");
    }

    /// <inheritdoc/>
    public IEnumerator<string?> GetEnumerator()
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT value FROM list;
                """;

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string? value = null;

                    if (!reader.IsDBNull(0))
                    {
                        value = reader.GetString(0);
                    }

                    yield return value;
                }
            }
        }
    }

    /// <inheritdoc/>
    public int IndexOf(string? item)
    {
        CheckDisposed();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ROWID, * FROM list WHERE value = @value LIMIT 1;
                """;

            command.Parameters.AddWithValue("value", item);

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return reader.GetInt32(0) - 1;
                }
            }
        }
        return -1;
    }

    /// <inheritdoc/>
    [Obsolete("This database does not support this action.")]
    public void Insert(int index, string? item)
    {
        throw new NotSupportedException("This database does not support this action.");
    }

    /// <summary>
    /// Removes all items that is equals to the specified string.
    /// </summary>
    /// <param name="item">The input string to remove in this list.</param>
    public bool Remove(string? item)
    {
        CheckDisposed();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM list WHERE value = @value;
                """;

            command.Parameters.AddWithValue("value", item);

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        CheckDisposed();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM list WHERE rowid = @index;
                """;

            command.Parameters.AddWithValue("index", index + 1);

            using (var reader = command.ExecuteReader())
            {
                reader.Read();
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                connection.Dispose();
                SqliteConnection.ClearPool(connection);
            }

            disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Não altere este código. Coloque o código de limpeza no método 'Dispose(bool disposing)'
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
