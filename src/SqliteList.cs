using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace CypherPotato.SqliteCollections;

/// <summary>
/// Provides an Sqlite based data-persistant list of strings.
/// </summary>
public sealed class SqliteList : IDisposable, IList<string?> {
    private readonly SqliteConnection connection;
    private bool disposedValue;
    private readonly string tableName;
    private readonly object queryLocker = new object ();

    /// <summary>
    /// Opens an new read-only <see cref="SqliteList"/> instance in the specified
    /// database name.
    /// </summary>
    /// <param name="databaseName">The database name (connection string).</param>
    /// <param name="tableName">The database table name.</param>
    public static SqliteList OpenRead ( string databaseName, string tableName = "list" ) {
        return new SqliteList ( databaseName, tableName, true );
    }

    /// <summary>
    /// Opens an new read-only <see cref="SqliteList"/> instance using the specified
    /// <see cref="SqliteConnection"/>.
    /// </summary>
    /// <param name="connection">The <see cref="SqliteConnection"/> to use.</param>
    /// <param name="tableName">The database table name.</param>
    public static SqliteList OpenRead ( SqliteConnection connection, string tableName = "list" ) {
        return new SqliteList ( connection, tableName, true );
    }

    /// <summary>
    /// Opens an new <see cref="SqliteList"/> instance in the specified
    /// database name.
    /// </summary>
    /// <param name="databaseName">The database name (connection string).</param>
    /// <param name="tableName">The database table name.</param>
    public static SqliteList Open ( string databaseName, string tableName = "list" ) {
        return new SqliteList ( databaseName, tableName, false );
    }

    /// <summary>
    /// Opens an new <see cref="SqliteList"/> instance using the specified
    /// <see cref="SqliteConnection"/>.
    /// </summary>
    /// <param name="connection">The <see cref="SqliteConnection"/> to use.</param>
    /// <param name="tableName">The database table name.</param>
    public static SqliteList Open ( SqliteConnection connection, string tableName = "list" ) {
        return new SqliteList ( connection, tableName, false );
    }

    /// <summary>
    /// Gets the inner <see cref="SqliteConnection"/>.
    /// </summary>
    public SqliteConnection Connection { get => connection; }

    /// <summary>
    /// Gets the synchronization root for the current instance.
    /// </summary>
    /// <value>An object that can be used to synchronize access to the <see cref="SqliteList"/>.</value>
    public object SyncRoot { get => queryLocker; }

    /// <summary>
    /// Gets or sets an value based on their index.
    /// </summary>
    /// <param name="index">The zero-based object index.</param>
    public string? this [ int index ] {
        get {
            CheckDisposed ();

            lock (queryLocker)
                using (SqliteCommand command = connection.CreateCommand ()) {
                    command.CommandText = $"""
                        SELECT value FROM "{tableName}" WHERE rowid = @index;
                        """;

                    command.Parameters.AddWithValue ( "index", index );

                    using (var reader = command.ExecuteReader ()) {
                        if (reader.Read ()) {
                            if (reader.IsDBNull ( 0 )) {
                                return null;
                            }
                            return reader.GetString ( 0 );
                        }
                    }
                }

            return null;
        }
        set {
            CheckDisposed ();
            CheckReadonly ();

            lock (queryLocker)
                using (SqliteCommand command = connection.CreateCommand ()) {
                    command.CommandText = $"""
                        INSERT OR REPLACE INTO "{tableName}" (rowid, value) VALUES (@index, @value);
                        """;

                    command.Parameters.AddWithValue ( "index", index );
                    command.Parameters.AddWithValue ( "value", value );
                    command.ExecuteNonQuery ();
                }
        }
    }

    /// <inheritdoc/>
    public int Count {
        get {
            CheckDisposed ();

            lock (queryLocker)
                using (SqliteCommand command = connection.CreateCommand ()) {
                    command.CommandText = $"""
                        SELECT COUNT(value) FROM "{tableName}";
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
    /// Gets an boolean indicating if this <see cref="SqliteList"/> is read-only.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    internal SqliteList ( string databaseName, string tableName, bool isReadOnly = true ) {
        IsReadOnly = isReadOnly;
        this.tableName = tableName;

        if (!databaseName.EndsWith ( ".db", StringComparison.CurrentCultureIgnoreCase ))
            databaseName += ".db";

        connection = new SqliteConnection ( $"Data Source={databaseName};" );
        connection.Open ();

        EnsureListTable ();
    }

    internal SqliteList ( SqliteConnection connection, string tableName, bool isReadOnly ) {
        IsReadOnly = isReadOnly;
        this.tableName = tableName;
        this.connection = connection;

        EnsureListTable ();
    }

    void EnsureListTable () {
        lock (queryLocker)
            using (DbCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS "{tableName}" (
                        "value"	TEXT
                    );
                    """;

                command.ExecuteNonQuery ();
            }
    }

    void CheckDisposed () {
        if (disposedValue)
            throw new ObjectDisposedException ( nameof ( SqliteList ) );
    }

    void CheckReadonly () {
        if (IsReadOnly)
            throw new InvalidOperationException ( "Cannot modify this dictionary: this database was openned in read-only mode." );
    }

    /// <inheritdoc/>
    public void Add ( string? item ) {
        CheckDisposed ();
        CheckReadonly ();

        lock (queryLocker)
            using (SqliteCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    INSERT INTO "{tableName}" (value) VALUES (@value);
                    """;

                command.Parameters.AddWithValue ( "value", item );
                command.ExecuteNonQuery ();
            }
    }

    /// <summary>
    /// Adds an item to the end of this <see cref="ICollection"/>.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add ( object? item ) => Add ( item?.ToString () );

    /// <inheritdoc/>
    public void Clear () {
        CheckDisposed ();
        CheckReadonly ();

        lock (queryLocker)
            using (SqliteCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    DELETE FROM "{tableName}";
                    """;
                command.ExecuteNonQuery ();
            }
    }

    /// <inheritdoc/>
    public bool Contains ( string? item ) {
        CheckDisposed ();

        lock (queryLocker)
            using (SqliteCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT * FROM "{tableName}" WHERE value = @value LIMIT 1;
                    """;

                command.Parameters.AddWithValue ( "value", item );

                using (var reader = command.ExecuteReader ()) {
                    if (reader.Read ()) {
                        return true;
                    }
                }
            }
        return false;
    }

    /// <inheritdoc/>
    public void CopyTo ( string? [] array, int arrayIndex ) {
        int index = arrayIndex;
        foreach (var item in this) {
            if (index >= array.Length)
                break;
            array [ index++ ] = item;
        }
    }

    /// <summary>
    /// Returns an iterator that iterates the collection values and row IDs from this collection.
    /// </summary>
    public IEnumerable<KeyValuePair<int, string?>> AsIdentifiedEnumerable () {
        lock (queryLocker)
            using (SqliteCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT ROWID, value FROM "{tableName}";
                    """;

                using (var reader = command.ExecuteReader ()) {
                    while (reader.Read ()) {
                        int key = -1;
                        string? value = null;

                        if (!reader.IsDBNull ( 0 )) {
                            key = reader.GetInt32 ( 0 );
                            value = reader.GetString ( 1 );
                        }

                        yield return new KeyValuePair<int, string?> ( key, value );
                    }
                }
            }
    }

    /// <inheritdoc/>
    public IEnumerator<string?> GetEnumerator () {
        lock (queryLocker)
            using (SqliteCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT value FROM "{tableName}";
                    """;

                using (var reader = command.ExecuteReader ()) {
                    while (reader.Read ()) {
                        string? value = null;

                        if (!reader.IsDBNull ( 0 )) {
                            value = reader.GetString ( 0 );
                        }

                        yield return value;
                    }
                }
            }
    }

    /// <inheritdoc/>
    public int IndexOf ( string? item ) {
        CheckDisposed ();
        lock (queryLocker)
            using (SqliteCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT ROWID, * FROM "{tableName}" WHERE value = @value LIMIT 1;
                    """;

                command.Parameters.AddWithValue ( "value", item );

                using (var reader = command.ExecuteReader ()) {
                    if (reader.Read ()) {
                        return reader.GetInt32 ( 0 );
                    }
                }
            }
        return -1;
    }

    /// <summary>
    /// Removes all items that is equals to the specified string.
    /// </summary>
    /// <param name="item">The input string to remove in this list.</param>
    public bool Remove ( string? item ) {
        CheckDisposed ();

        lock (queryLocker)
            using (SqliteCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    DELETE FROM "{tableName}" WHERE value = @value;
                    """;

                command.Parameters.AddWithValue ( "value", item );

                using (var reader = command.ExecuteReader ()) {
                    if (reader.Read ()) {
                        return true;
                    }
                }
            }
        return false;
    }

    /// <inheritdoc/>
    public void RemoveAt ( int index ) {
        CheckDisposed ();

        lock (queryLocker)
            using (SqliteCommand command = connection.CreateCommand ()) {
                command.CommandText = $"""
                    DELETE FROM "{tableName}" WHERE rowid = @index;
                    """;

                command.Parameters.AddWithValue ( "index", index );

                command.ExecuteNonQuery ();
            }
    }

    IEnumerator IEnumerable.GetEnumerator () {
        return GetEnumerator ();
    }

    private void Dispose ( bool disposing ) {
        if (!disposedValue) {
            if (disposing) {
                connection.Dispose ();
            }

            disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose () {
        // Não altere este código. Coloque o código de limpeza no método 'Dispose(bool disposing)'
        Dispose ( disposing: true );
        GC.SuppressFinalize ( this );
    }

    void IList<string?>.Insert ( int index, string? item ) {
        throw new NotSupportedException ();
    }
}
