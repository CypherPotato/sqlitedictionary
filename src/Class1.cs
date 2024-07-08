using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using System.Diagnostics.CodeAnalysis;

public sealed class SqliteDictionary : IDisposable, IDictionary<string, string?>
{
    private SqliteConnection connection;

    public static SqliteDictionary OpenRead(string databaseName)
    {
        return new SqliteDictionary(databaseName, true);
    }

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

    public string? this[string key]
    {
        get
        {
            if (TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }
        set
        {
            CheckReadonly();
            CheckKey(key);
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

    public ICollection<string> Keys
    {
        get
        {
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

    public ICollection<string?> Values
    {
        get
        {
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

    public int Count
    {
        get
        {
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

    public bool IsReadOnly { get; }

    public void Add(string key, string? value)
    {
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

    public void Add(KeyValuePair<string, string?> item)
    {
        Add(item.Key, item.Value);
    }

    public void Clear()
    {
        CheckReadonly();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM base;
                """;
            command.ExecuteNonQuery();
        }
    }

    public bool Contains(KeyValuePair<string, string?> item)
    {
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

    public bool ContainsKey(string key)
    {
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

    public void CopyTo(KeyValuePair<string, string?>[] array, int arrayIndex)
    {
        throw new NotImplementedException("This database does not support this action.");
    }

    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT key, value FROM base;
                """;

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
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

    public bool Remove(string key)
    {
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

    public bool Remove(KeyValuePair<string, string?> item)
    {
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

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string? value)
    {
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

    public void Dispose()
    {
        connection.Dispose();
        SqliteConnection.ClearPool(connection);
    }
}
