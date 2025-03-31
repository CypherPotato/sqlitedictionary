using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace CypherPotato.SqliteCollections;

/// <summary>
/// Represents a database set of entities.
/// </summary>
/// <typeparam name="TEntity">The type of entity in the set.</typeparam>
public class DbCollection<TEntity> :
    ICollection<TEntity>,
    IReadOnlyCollection<TEntity>,
    IAsyncEnumerable<TEntity>,
    IDisposable,
    IAsyncDisposable where TEntity : notnull {

    private ConcurrentQueue<DbOperation> pendingOperations = new ConcurrentQueue<DbOperation> ();
    private SemaphoreSlim operationRunner = new SemaphoreSlim ( 1 );
    private SqliteConnection sqlConnection;
    private string tableName;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbCollection{TEntity}"/> class.
    /// </summary>
    /// <param name="sqlConnection">The SQLite connection to use.</param>
    /// <param name="tableName">The name of the table to use. If null, a default name will be generated.</param>
    public DbCollection ( SqliteConnection sqlConnection, string? tableName = null ) {
        this.sqlConnection = sqlConnection;
        this.tableName = tableName ?? $".dbstore.{typeof ( TEntity ).Name}";

        EnsureDatabase ();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DbCollection{TEntity}"/> class with a default connection.
    /// </summary>
    public DbCollection () : this ( new SqliteConnection ( "Data Source=app-database.db" ), null ) {
    }

    /// <summary>
    /// Gets or sets the SQLite connection used by the <see cref="DbCollection{TEntity}"/>.
    /// </summary>
    protected SqliteConnection Connection {
        get => sqlConnection;
        set => sqlConnection = value;
    }

    /// <summary>
    /// Gets or sets the name of the table used by the <see cref="DbCollection{TEntity}"/>.
    /// </summary>
    protected string TableName {
        get => tableName;
        set => tableName = value;
    }

    /// <summary>
    /// Serializes an entity to a byte array.
    /// </summary>
    /// <param name="entity">The entity to serialize.</param>
    /// <returns>The serialized entity as a byte array.</returns>
    protected virtual byte [] SerializeEntity ( TEntity entity ) {
        return JsonSerializer.SerializeToUtf8Bytes ( entity );
    }

    /// <summary>
    /// Deserializes a byte array to an entity.
    /// </summary>
    /// <param name="bytes">The byte array to deserialize.</param>
    /// <returns>The deserialized entity.</returns>
    protected virtual TEntity DeserializeEntity ( byte [] bytes ) {
        return JsonSerializer.Deserialize<TEntity> ( bytes )!;
    }

    /// <summary>
    /// Checks if two entities are equal.
    /// </summary>
    /// <param name="a">The first entity.</param>
    /// <param name="b">The second entity.</param>
    /// <returns>True if the entities are equal, false otherwise.</returns>
    protected virtual bool EntityEquals ( TEntity a, TEntity b ) {
        return a.Equals ( b );
    }

    /// <summary>
    /// Ensures the database and table exist, optionally enabling Write-Ahead Logging (WAL) mode.
    /// </summary>
    /// <param name="useJournalWal">If true, enables Write-Ahead Logging (WAL) mode for the database; otherwise, false.</param>
    protected void EnsureDatabase ( bool useJournalWal = false ) {
        sqlConnection.Open ();

        using (DbCommand command = sqlConnection.CreateCommand ()) {
            command.CommandText = $"""
                CREATE TABLE IF NOT EXISTS "{tableName}" (
                    "_id"  INTEGER PRIMARY KEY,
                    "data" BLOB NOT NULL
                );
                """;

            if (useJournalWal) {
                command.CommandText += "\nPRAGMA journal_mode=WAL;";
            }

            command.ExecuteNonQuery ();
        }
    }

    private void RunPendingOperations ( bool blocking = false, CancellationToken cancellation = default ) {

        int carry = 0;

        string GetCommandName ( string name ) {
            return $"@_c{Interlocked.Increment ( ref carry )}_{name}";
        }

        if (blocking) {
            operationRunner.Wait ( cancellation );
        }
        else {
            if (operationRunner.Wait ( 0 ) == false)
                return;
        }

        try {

il_dequeue:
            StringBuilder commandBuilder = new StringBuilder ();
            int currentBatch = 0;
            Dictionary<string, object> commandParameters = new Dictionary<string, object> ();

            while (pendingOperations.TryDequeue ( out DbOperation? pendingOperation )) {

                currentBatch++;
                if (pendingOperation is InsertEntityDbOperation insertOperation) {
                    string paramData = GetCommandName ( "data" );
                    commandBuilder.AppendLine ( $"INSERT INTO \"{tableName}\" (data) VALUES ({paramData});" );
                    commandParameters.Add ( paramData, insertOperation.EntityData );
                    ;
                }
                else if (pendingOperation is UpdateEntityDbOperation updateOperation) {
                    string paramData = GetCommandName ( "data" );
                    string paramId = GetCommandName ( "id" );
                    commandBuilder.AppendLine ( $"INSERT OR REPLACE INTO \"{tableName}\" (_id, data) VALUES ({paramId}, {paramData});" );
                    commandParameters.Add ( paramId, updateOperation.RowId );
                    commandParameters.Add ( paramData, updateOperation.EntityData );
                    ;
                }
                else if (pendingOperation is DeleteEntityDbOperation deleteOperation) {
                    string paramId = GetCommandName ( "id" );
                    commandBuilder.AppendLine ( $"DELETE FROM \"{tableName}\" WHERE _id = {paramId};" );
                    commandParameters.Add ( paramId, deleteOperation.RowId );
                    ;
                }
                else if (pendingOperation is ClearDbOperation clearOperation) {
                    commandBuilder.AppendLine ( $"DELETE FROM \"{tableName}\";" );
                    ;
                }

                if (currentBatch >= 1_000)
                    break;
            }

            using (var transaction = sqlConnection.BeginTransaction ()) {
                string commandText = commandBuilder.ToString ();

                try {
                    using (var command = new SqliteCommand ( commandText, sqlConnection, transaction )) {
                        foreach (var parameter in commandParameters) {
                            command.Parameters.AddWithValue ( parameter.Key, parameter.Value );
                        }

                        command.ExecuteNonQuery ();
                    }

                    transaction.Commit ();
                }
                catch {
                    transaction.Rollback ();
                }
            }

            if (!pendingOperations.IsEmpty) {
                goto il_dequeue;
            }
        }
        finally {
            operationRunner.Release ();
        }
    }

    /// <summary>
    /// Gets the number of entities in the set.
    /// </summary>
    public int Count {
        get {
            using (SqliteCommand command = sqlConnection.CreateCommand ()) {
                command.CommandText = $"""
                    SELECT COUNT(data) FROM "{tableName}";
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
    /// Gets a value indicating whether the set is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Adds an entity to the set.
    /// </summary>
    /// <param name="item">The entity to add.</param>
    public void Add ( TEntity item ) {
        pendingOperations.Enqueue ( new InsertEntityDbOperation () {
            EntityData = SerializeEntity ( item )
        } );
        RunPendingOperations ();
    }

    /// <summary>
    /// Adds a range of entities to the set.
    /// </summary>
    /// <param name="items">The entities to add.</param>
    public void AddRange ( IEnumerable<TEntity> items ) {
        foreach (var item in items) {
            pendingOperations.Enqueue ( new InsertEntityDbOperation () {
                EntityData = SerializeEntity ( item )
            } );
        }
        RunPendingOperations ();
    }

    /// <summary>
    /// Adds or updates an entity in the set.
    /// </summary>
    /// <param name="item">The entity to add or update.</param>
    public void AddOrUpdate ( TEntity item ) {
        long index = IndexOf ( item );
        if (index >= 0) {
            Update ( index, item );
        }
        else {
            Add ( item );
        }
    }

    /// <summary>
    /// Updates an entity in the set.
    /// </summary>
    /// <param name="id">The ID of the entity to update.</param>
    /// <param name="item">The updated entity.</param>
    public void Update ( long id, TEntity item ) {
        pendingOperations.Enqueue ( new UpdateEntityDbOperation () {
            EntityData = SerializeEntity ( item ),
            RowId = id
        } );
        RunPendingOperations ();
    }

    /// <summary>
    /// Updates all entities in the set that match a predicate.
    /// </summary>
    /// <param name="predicate">The predicate to match.</param>
    /// <param name="updateFunction">The function to update the entities.</param>
    /// <returns>The number of entities updated.</returns>
    public int UpdateAll ( Func<TEntity, bool> predicate, Func<TEntity, TEntity> updateFunction ) {
        int count = 0;
        foreach (var value in GetIdentifiedEnumerator ()) {
            if (predicate ( value.Entity )) {
                var updatedEntity = updateFunction ( value.Entity );
                pendingOperations.Enqueue ( new UpdateEntityDbOperation () {
                    EntityData = SerializeEntity ( updatedEntity ),
                    RowId = value.Id
                } );
                count++;
            }
        }
        RunPendingOperations ();
        return count;
    }

    /// <summary>
    /// Finds an entity by it's database id.
    /// </summary>
    /// <param name="id">The identifier of the entity to find.</param>
    /// <returns>The found entity, or the default value of <typeparamref name="TEntity"/> if not found.</returns>
    public TEntity? Find ( long id ) {
        using (SqliteCommand command = sqlConnection.CreateCommand ()) {
            command.CommandText = $"""
                SELECT data FROM "{tableName}" WHERE _id = @id;
                """;

            command.Parameters.AddWithValue ( "@id", id );

            using (var reader = command.ExecuteReader ()) {
                if (reader.Read ()) {
                    var value = (byte []) reader.GetValue ( 0 );
                    return DeserializeEntity ( value );
                }
            }
        }
        return default;
    }

    /// <summary>
    /// Clears all entities from the set.
    /// </summary>
    public void Clear () {
        pendingOperations.Enqueue ( new ClearDbOperation () );
        RunPendingOperations ();
    }

    /// <summary>
    /// Gets the id of an entity in the set.
    /// </summary>
    /// <param name="item">The entity to find.</param>
    /// <returns>The id of the entity, or -1 if not found.</returns>
    public long IndexOf ( TEntity item ) {
        foreach (var value in GetIdentifiedEnumerator ()) {
            if (value.Entity.Equals ( item ))
                return value.Id;
        }
        return -1;
    }

    /// <summary>
    /// Checks if the set contains an entity.
    /// </summary>
    /// <param name="item">The entity to check.</param>
    /// <returns>True if the entity is in the set, false otherwise.</returns>
    public bool Contains ( TEntity item ) {
        return IndexOf ( item ) >= 0;
    }

    /// <summary>
    /// Copies the entities in the set to an array.
    /// </summary>
    /// <param name="array">The array to copy to.</param>
    /// <param name="arrayIndex">The index in the array to start copying at.</param>
    public void CopyTo ( TEntity [] array, int arrayIndex ) {
        int index = arrayIndex;
        foreach (var item in this) {
            if (index >= array.Length)
                break;
            array [ index++ ] = item;
        }
    }

    /// <summary>
    /// Waits for any pending database operations to complete.
    /// </summary>
    public void Flush () {
        RunPendingOperations ( blocking: true );
    }

    /// <summary>
    /// Waits for any pending database operations to complete with a specified timeout.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for pending operations to complete, see <see cref="TimeSpan"/>.</param>
    public void Flush ( TimeSpan timeout ) {
        CancellationTokenSource cancellationSource = new CancellationTokenSource ( timeout );
        RunPendingOperations ( blocking: true, cancellationSource.Token );
    }

    /// <summary>
    /// Waits for any pending database operations to complete with a specified cancellation token.
    /// </summary>
    /// <param name="cancellation">The token to monitor for cancellation requests, see <see cref="CancellationToken"/>.</param>
    public void Flush ( CancellationToken cancellation = default ) {
        RunPendingOperations ( blocking: true, cancellation );
    }

    /// <inheritdoc/>
    public void Dispose () {
        Flush ();
        sqlConnection.Dispose ();
        operationRunner.Dispose ();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync () {
        Flush ();
        await sqlConnection.DisposeAsync ();
        operationRunner.Dispose ();
    }

    /// <inheritdoc/>
    public IEnumerator<TEntity> GetEnumerator () {
        using (var command = sqlConnection.CreateCommand ()) {
            command.CommandText = $"SELECT data FROM \"{tableName}\"";

            using (var reader = command.ExecuteReader ()) {
                while (reader.Read ()) {
                    byte [] result = (byte []) reader.GetValue ( 0 );
                    TEntity entity = DeserializeEntity ( result );
                    yield return entity;
                }
            }
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerator<TEntity> GetAsyncEnumerator ( CancellationToken cancellationToken = default ) {
        using (var command = sqlConnection.CreateCommand ()) {
            command.CommandText = $"SELECT data FROM \"{tableName}\"";

            using (var reader = await command.ExecuteReaderAsync ( cancellationToken )) {
                while (await reader.ReadAsync ( cancellationToken )) {

                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    byte [] result = (byte []) reader.GetValue ( 0 );
                    TEntity entity = DeserializeEntity ( result );
                    yield return entity;
                }
            }
        }
    }

    /// <summary>
    /// Gets an enumerator for the entities in the set, along with their IDs.
    /// </summary>
    /// <returns>An enumerator for the entities with their IDs.</returns>
    public IEnumerable<EntityIdentifier<TEntity>> GetIdentifiedEnumerator () {
        using (var command = sqlConnection.CreateCommand ()) {
            command.CommandText = $"SELECT _id, data FROM \"{tableName}\"";

            using (var reader = command.ExecuteReader ()) {
                while (reader.Read ()) {
                    long rowid = reader.GetInt64 ( 0 );
                    byte [] result = (byte []) reader.GetValue ( 1 );
                    TEntity entity = DeserializeEntity ( result );

                    yield return new EntityIdentifier<TEntity> ( rowid, entity );
                }
            }
        }
    }

    /// <summary>
    /// Gets an asynchronous enumerator for the entities in the set, along with their IDs.
    /// </summary>
    /// <param name="cancellation">A <see cref="CancellationToken"/> to observe for cancellation requests.</param>
    /// <returns>An asynchronous enumerator for the entities with their IDs.</returns>
    public async IAsyncEnumerable<EntityIdentifier<TEntity>> GetAsyncIdentifiedEnumerator ( [EnumeratorCancellation] CancellationToken cancellation = default ) {
        using (var command = sqlConnection.CreateCommand ()) {
            command.CommandText = $"SELECT _id, data FROM \"{tableName}\"";

            using (var reader = await command.ExecuteReaderAsync ( cancellation )) {
                while (await reader.ReadAsync ( cancellation )) {

                    if (cancellation.IsCancellationRequested)
                        yield break;

                    long rowid = reader.GetInt64 ( 0 );
                    byte [] result = (byte []) reader.GetValue ( 1 );
                    TEntity entity = DeserializeEntity ( result );

                    yield return new EntityIdentifier<TEntity> ( rowid, entity );
                }
            }
        }
    }

    /// <summary>
    /// Removes all entities from the set that match a predicate.
    /// </summary>
    /// <param name="predicate">The predicate to match.</param>
    /// <returns>The number of entities removed.</returns>
    public int RemoveAll ( Func<TEntity, bool> predicate ) {
        int toRemove = 0;
        foreach (var value in GetIdentifiedEnumerator ()) {
            if (predicate ( value.Entity )) {
                pendingOperations.Enqueue ( new DeleteEntityDbOperation () {
                    RowId = value.Id
                } );
                toRemove++;
            }
        }
        RunPendingOperations ();
        return toRemove;
    }

    /// <summary>
    /// Removes an entity from the set.
    /// </summary>
    /// <param name="item">The entity to remove.</param>
    /// <returns>True if the entity was removed, false otherwise.</returns>
    public bool Remove ( TEntity item ) {
        long index = IndexOf ( item );
        if (index < 0) {
            return false;
        }
        pendingOperations.Enqueue ( new DeleteEntityDbOperation () {
            RowId = index
        } );
        RunPendingOperations ();
        return true;
    }

    /// <summary>
    /// Removes the entity at the specified ID from the set.
    /// </summary>
    /// <param name="id">The ID of the entity to remove.</param>
    public void RemoveAt ( long id ) {
        pendingOperations.Enqueue ( new DeleteEntityDbOperation () {
            RowId = id
        } );
        RunPendingOperations ();
    }

    IEnumerator IEnumerable.GetEnumerator () {
        return GetEnumerator ();
    }
}
