using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Provides an Sqlite based data-persistant dictionary of strings.
/// </summary>
public sealed class SqliteDictionary : IDisposable, IDictionary<string, string?>
{
    private SqliteConnection connection;
    private bool disposed;

    /// <summary>
    /// Opens an new read-only <see cref="SqliteDictionary"/> instance in the specified
    /// database name.
    /// </summary>
    /// <param name="databaseName">The database name (connection string).</param>
    public static SqliteDictionary OpenRead(string databaseName)
    {
        return new SqliteDictionary(databaseName, true);
    }

    /// <summary>
    /// Opens an new <see cref="SqliteDictionary"/> instance in the specified
    /// database name.
    /// </summary>
    /// <param name="databaseName">The database name (connection string).</param>
    public static SqliteDictionary Open(string databaseName)
    {
        return new SqliteDictionary(databaseName, false);
    }

    internal SqliteDictionary(string databaseName, bool isReadOnly = true)
    {
        this.IsReadOnly = isReadOnly;

        if (!databaseName.EndsWith(".db", StringComparison.CurrentCultureIgnoreCase))
            databaseName += ".db";

        connection = new SqliteConnection($"Data Source={databaseName};");
        connection.Open();

        EnsureDictionaryTable();
    }

    void EnsureDictionaryTable()
    {
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS "base" (
                	"key"	TEXT NOT NULL UNIQUE,
                	"value"	TEXT,
                	PRIMARY KEY("key")
                );
                """;

            command.ExecuteNonQuery();
        }
    }

    void CheckDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(SqliteDictionary));
    }

    void CheckReadonly()
    {
        if (IsReadOnly) throw new InvalidOperationException("Cannot modify this dictionary: this database was openned in read-only mode.");
    }

    void CheckKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("The key cannot be empty or null.");
        }
    }

    /// <summary>
    /// Gets or sets an value based on their key.
    /// </summary>
    /// <param name="key">The object key.</param>
    public string? this[string key]
    {
        get
        {
            CheckDisposed();
            if (TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }
        set
        {
            CheckDisposed();
            CheckReadonly();
            CheckKey(key);

            if (value is null)
            {
                Remove(key);
            }
            else
            {
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = """
                        INSERT OR REPLACE INTO base (key, value) VALUES (@key, @value);
                        """;

                    command.Parameters.AddWithValue("key", key);
                    command.Parameters.AddWithValue("value", value);
                    command.ExecuteNonQuery();
                }
            }
        }
    }

    /// <summary>
    /// Gets an collection of keys defined in this database.
    /// </summary>
    public ICollection<string> Keys
    {
        get
        {
            CheckDisposed();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT key FROM base;
                    """;

                List<string> result = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        result.Add(reader.GetString(0));
                    }
                }

                return result;
            }
        }
    }

    /// <summary>
    /// Gets an collection of values defined in this database.
    /// </summary>
    public ICollection<string?> Values
    {
        get
        {
            CheckDisposed();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT value FROM base;
                    """;

                List<string?> result = new List<string?>();
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        result.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
                    }
                }

                return result;
            }
        }
    }

    /// <summary>
    /// Gets the count of value pairs in this database.
    /// </summary>
    public int Count
    {
        get
        {
            CheckDisposed();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT COUNT(key) FROM base;
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
    /// Gets an boolean indicating if this <see cref="SqliteDictionary"/> is read-only.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Adds an item to this database.
    /// </summary>
    /// <param name="key">The unique object key.</param>
    /// <param name="value">The object value.</param>
    public void Add(string key, string? value)
    {
        CheckDisposed();
        CheckReadonly();
        CheckKey(key);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO base (key, value) VALUES (@key, @value);
                """;

            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("value", value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Adds an item to this database.
    /// </summary>
    /// <param name="item">The pair of key and value to add.</param>
    public void Add(KeyValuePair<string, string?> item)
    {
        Add(item.Key, item.Value);
    }

    /// <summary>
    /// Clears and removes all items from this database.
    /// </summary>
    public void Clear()
    {
        CheckDisposed();
        CheckReadonly();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM base;
                """;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Checks if the specified key and value exists in the current database.
    /// </summary>
    /// <param name="item">The key-value-pair to check whether is defined or not.</param>
    public bool Contains(KeyValuePair<string, string?> item)
    {
        CheckDisposed();
        CheckKey(item.Key);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT * FROM base WHERE key = @key AND value = @value LIMIT 1;
                """;
            command.Parameters.AddWithValue("key", item.Key);
            command.Parameters.AddWithValue("value", item.Value);

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

    /// <summary>
    /// Checks if the specified key is defined in this database.
    /// </summary>
    /// <param name="key">The key to search.</param>
    public bool ContainsKey(string key)
    {
        CheckDisposed();
        CheckKey(key);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT * FROM base WHERE key = @key LIMIT 1;
                """;
            command.Parameters.AddWithValue("key", key);

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

    /// <summary>
    /// This method is not implemented and should not be used.
    /// </summary>
    public void CopyTo(KeyValuePair<string, string?>[] array, int arrayIndex)
    {
        throw new NotImplementedException("This database does not support this action.");
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT key, value FROM base;
                """;

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string key = reader.GetString(0);
                    string? value = null;

                    if (!reader.IsDBNull(1))
                    {
                        value = reader.GetString(1);
                    }

                    yield return new KeyValuePair<string, string?>(key, value);
                }
            }
        }
    }

    /// <summary>
    /// Tries to remove the specified key from this database.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    public bool Remove(string key)
    {
        CheckDisposed();
        CheckReadonly();
        CheckKey(key);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM base WHERE key = @key;
                """;
            command.Parameters.AddWithValue("key", key);
            command.ExecuteNonQuery();

            using (var reader = command.ExecuteReader())
            {
                return reader.RecordsAffected >= 1;
            }
        }
    }

    /// <summary>
    /// Tries to remove the specified key and value from this database.
    /// </summary>
    /// <param name="item">The value-key pair to remove.</param>
    public bool Remove(KeyValuePair<string, string?> item)
    {
        CheckDisposed();
        CheckReadonly();
        CheckKey(item.Key);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM base WHERE key = @key AND value = @value;
                """;
            command.Parameters.AddWithValue("key", item.Key);
            command.Parameters.AddWithValue("value", item.Value);
            command.ExecuteNonQuery();

            using (var reader = command.ExecuteReader())
            {
                return reader.RecordsAffected >= 1;
            }
        }
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    public (bool CouldGet, string? Value) TryGetValue(string key)
    {
        if (TryGetValue(key, out var value))
        {
            return (true, value);
        }
        return (false, null);
    }

    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string? value)
    {
        CheckDisposed();
        CheckKey(key);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT value FROM base WHERE key = @key LIMIT 1;
                """;
            command.Parameters.AddWithValue("key", key);

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    value = reader.IsDBNull(0) ? null : reader.GetString(0);
                    return true;
                }
            }
        }
        value = null;
        return false;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        disposed = true;
        connection.Dispose();
        SqliteConnection.ClearPool(connection);
    }
}
