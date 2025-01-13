// The Sisk Framework source code
// Copyright (c) 2024- PROJECT PRINCIPIUM and all Sisk contributors
//
// The code below is licensed under the MIT license as
// of the date of its publication, available at
//
// File name:   SqliteDictionary.cs
// Repository:  https://github.com/sisk-http/core

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace CypherPotato.SqliteCollections;

/// <summary>
/// Provides an Sqlite based data-persistant dictionary of strings.
/// </summary>
public class SqliteDictionary : IDisposable, IDictionary<string, string?> {
    private SqliteConnection connection;
    private bool disposed;
    private string tableName;
    private object queryLocker = new object ();

    /// <summary>
    /// Opens an new read-only <see cref="SqliteDictionary"/> instance in the specified
    /// database name.
    /// </summary>
    /// <param name="databaseName">The database name (connection string).</param>
    /// <param name="tableName">The database table name.</param>
    public static SqliteDictionary OpenRead ( string databaseName, string tableName = "base" ) {
        return new SqliteDictionary ( databaseName, tableName, true );
    }

    /// <summary>
    /// Opens an new <see cref="SqliteDictionary"/> instance in the specified
    /// database name.
    /// </summary>
    /// <param name="databaseName">The database name (connection string).</param>
    /// <param name="tableName">The database table name.</param>
    public static SqliteDictionary Open ( string databaseName, string tableName = "base" ) {
        return new SqliteDictionary ( databaseName, tableName, false );
    }

    internal SqliteDictionary ( string databaseName, string tableName, bool isReadOnly = true ) {
        this.IsReadOnly = isReadOnly;
        this.tableName = tableName;

        if (!databaseName.EndsWith ( ".db", StringComparison.CurrentCultureIgnoreCase ))
            databaseName += ".db";

        this.connection = new SqliteConnection ( $"Data Source={databaseName};" );
        this.connection.Open ();

        this.EnsureDictionaryTable ();
    }

    void EnsureDictionaryTable () {
        lock (this.queryLocker)
            using (DbCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS "{this.tableName}" (
                        "key"	TEXT NOT NULL UNIQUE,
                        "value"	TEXT,
                        PRIMARY KEY("key")
                    );
                    """;

                command.ExecuteNonQuery ();
            }
    }

    void CheckDisposed () {
        if (this.disposed)
            throw new ObjectDisposedException ( nameof ( SqliteDictionary ) );
    }

    void CheckReadonly () {
        if (this.IsReadOnly)
            throw new InvalidOperationException ( "Cannot modify this dictionary: this database was openned in read-only mode." );
    }

    void CheckKey ( string key ) {
        if (string.IsNullOrEmpty ( key )) {
            throw new InvalidOperationException ( "The key cannot be empty or null." );
        }
    }

    /// <summary>
    /// Gets the dictionary <see cref="TableName"/>.
    /// </summary>
    public string TableName { get => this.tableName; }

    /// <summary>
    /// Gets or sets an value based on their key.
    /// </summary>
    /// <param name="key">The object key.</param>
    public string? this [ string key ] {
        get {
            this.CheckDisposed ();
            if (this.TryGetValue ( key, out var value )) {
                return value;
            }
            return null;
        }
        set {
            this.CheckDisposed ();
            this.CheckReadonly ();
            this.CheckKey ( key );

            if (value is null) {
                this.Remove ( key );
            }
            else {
                lock (this.queryLocker)
                    using (SqliteCommand command = this.connection.CreateCommand ()) {
                        command.CommandText = $"""
                        INSERT OR REPLACE INTO "{this.tableName}" (key, value) VALUES (@key, @value);
                        """;

                        command.Parameters.AddWithValue ( "key", key );
                        command.Parameters.AddWithValue ( "value", value );
                        command.ExecuteNonQuery ();
                    }
            }
        }
    }

    /// <summary>
    /// Gets an collection of keys defined in this database.
    /// </summary>
    public ICollection<string> Keys {
        get {
            this.CheckDisposed ();

            lock (this.queryLocker)
                using (SqliteCommand command = this.connection.CreateCommand ()) {
                    command.CommandText = $"""
                        SELECT key FROM "{this.tableName}";
                        """;

                    List<string> result = new List<string> ();
                    using (var reader = command.ExecuteReader ()) {
                        if (reader.Read ()) {
                            result.Add ( reader.GetString ( 0 ) );
                        }
                    }

                    return result;
                }
        }
    }

    /// <summary>
    /// Gets an collection of values defined in this database.
    /// </summary>
    public ICollection<string?> Values {
        get {
            this.CheckDisposed ();
            lock (this.queryLocker)
                using (SqliteCommand command = this.connection.CreateCommand ()) {
                    command.CommandText = $"""
                        SELECT value FROM "{this.tableName}";
                        """;

                    List<string?> result = new List<string?> ();
                    using (var reader = command.ExecuteReader ()) {
                        if (reader.Read ()) {
                            result.Add ( reader.IsDBNull ( 0 ) ? null : reader.GetString ( 0 ) );
                        }
                    }

                    return result;
                }
        }
    }

    /// <summary>
    /// Gets the count of value pairs in this database.
    /// </summary>
    public int Count {
        get {
            this.CheckDisposed ();
            lock (this.queryLocker)
                using (SqliteCommand command = this.connection.CreateCommand ()) {
                    command.CommandText = $"""
                        SELECT COUNT(key) FROM "{this.tableName}";
                        """;

                    using (var reader = command.ExecuteReader ()) {
                        if (reader.Read ()) {
                            return reader.GetInt32 ( 0 );
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
    public void Add ( string key, string? value ) {
        this.CheckDisposed ();
        this.CheckReadonly ();
        this.CheckKey ( key );
        lock (this.queryLocker)
            using (SqliteCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    INSERT INTO "{this.tableName}" (key, value) VALUES (@key, @value);
                    """;

                command.Parameters.AddWithValue ( "key", key );
                command.Parameters.AddWithValue ( "value", value );
                command.ExecuteNonQuery ();
            }
    }

    /// <summary>
    /// Adds an item to this database.
    /// </summary>
    /// <param name="item">The pair of key and value to add.</param>
    public void Add ( KeyValuePair<string, string?> item ) {
        this.Add ( item.Key, item.Value );
    }

    /// <summary>
    /// Clears and removes all items from this database.
    /// </summary>
    public void Clear () {
        this.CheckDisposed ();
        this.CheckReadonly ();
        lock (this.queryLocker)
            using (SqliteCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    DELETE FROM "{this.tableName}";
                    """;
                command.ExecuteNonQuery ();
            }
    }

    /// <summary>
    /// Checks if the specified key and value exists in the current database.
    /// </summary>
    /// <param name="item">The key-value-pair to check whether is defined or not.</param>
    public bool Contains ( KeyValuePair<string, string?> item ) {
        this.CheckDisposed ();
        this.CheckKey ( item.Key );
        lock (this.queryLocker)
            using (SqliteCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT * FROM "{this.tableName}" WHERE key = @key AND value = @value LIMIT 1;
                    """;
                command.Parameters.AddWithValue ( "key", item.Key );
                command.Parameters.AddWithValue ( "value", item.Value );

                using (var reader = command.ExecuteReader ()) {
                    if (reader.Read ()) {
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
    public bool ContainsKey ( string key ) {
        this.CheckDisposed ();
        this.CheckKey ( key );
        lock (this.queryLocker)
            using (SqliteCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT * FROM "{this.tableName}" WHERE key = @key LIMIT 1;
                    """;
                command.Parameters.AddWithValue ( "key", key );

                using (var reader = command.ExecuteReader ()) {
                    if (reader.Read ()) {
                        return true;
                    }
                }
            }
        return false;
    }

    /// <summary>
    /// This method is not implemented and should not be used.
    /// </summary>
    public void CopyTo ( KeyValuePair<string, string?> [] array, int arrayIndex ) {
        throw new NotImplementedException ( "This database does not support this action." );
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator () {
        lock (this.queryLocker)
            using (SqliteCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT key, value FROM "{this.tableName}";
                    """;

                using (var reader = command.ExecuteReader ()) {
                    while (reader.Read ()) {
                        string key = reader.GetString ( 0 );
                        string? value = null;

                        if (!reader.IsDBNull ( 1 )) {
                            value = reader.GetString ( 1 );
                        }

                        yield return new KeyValuePair<string, string?> ( key, value );
                    }
                }
            }
    }

    /// <summary>
    /// Tries to remove the specified key from this database.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    public bool Remove ( string key ) {
        this.CheckDisposed ();
        this.CheckReadonly ();
        this.CheckKey ( key );
        lock (this.queryLocker)
            using (SqliteCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    DELETE FROM "{this.tableName}" WHERE key = @key;
                    """;
                command.Parameters.AddWithValue ( "key", key );
                command.ExecuteNonQuery ();

                using (var reader = command.ExecuteReader ()) {
                    return reader.RecordsAffected >= 1;
                }
            }
    }

    /// <summary>
    /// Tries to remove the specified key and value from this database.
    /// </summary>
    /// <param name="item">The value-key pair to remove.</param>
    public bool Remove ( KeyValuePair<string, string?> item ) {
        this.CheckDisposed ();
        this.CheckReadonly ();
        this.CheckKey ( item.Key );
        lock (this.queryLocker)
            using (SqliteCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    DELETE FROM "{this.tableName}" WHERE key = @key AND value = @value;
                    """;
                command.Parameters.AddWithValue ( "key", item.Key );
                command.Parameters.AddWithValue ( "value", item.Value );
                command.ExecuteNonQuery ();

                using (var reader = command.ExecuteReader ()) {
                    return reader.RecordsAffected >= 1;
                }
            }
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    public (bool CouldGet, string? Value) TryGetValue ( string key ) {
        if (this.TryGetValue ( key, out var value )) {
            return (true, value);
        }
        return (false, null);
    }

    /// <inheritdoc/>
    public bool TryGetValue ( string key, [MaybeNullWhen ( false )] out string? value ) {
        this.CheckDisposed ();
        this.CheckKey ( key );
        lock (this.queryLocker)
            using (SqliteCommand command = this.connection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT value FROM "{this.tableName}" WHERE key = @key LIMIT 1;
                    """;
                command.Parameters.AddWithValue ( "key", key );

                using (var reader = command.ExecuteReader ()) {
                    if (reader.Read ()) {
                        value = reader.IsDBNull ( 0 ) ? null : reader.GetString ( 0 );
                        return true;
                    }
                }
            }

        value = null;
        return false;
    }

    IEnumerator IEnumerable.GetEnumerator () {
        return this.GetEnumerator ();
    }

    /// <inheritdoc/>
    public void Dispose () {
        this.disposed = true;
        this.connection.Dispose ();
    }
}
