using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace CypherPotato.SqliteCollections;

/// <summary>
/// Represents a repository for storing and retrieving entities in a SQLite database.
/// </summary>
/// <typeparam name="TEntity">The type of entity stored in the repository.</typeparam>
public class SqliteRepository<TEntity> : IList<TEntity>, IDisposable where TEntity : notnull {
    private SqliteList? sqliteList;
    private bool disposedValue;

    /// <summary>
    /// Opens a new instance of the repository.
    /// </summary>
    /// <returns>A new instance of the repository.</returns>
    protected virtual SqliteList OpenRepository () {
        return SqliteList.Open ( "entity-repository", typeof ( TEntity ).Name );
    }

    /// <summary>
    /// Serializes an entity into a string.
    /// </summary>
    /// <param name="entity">The entity to serialize.</param>
    /// <returns>A string representation of the entity.</returns>
    protected virtual string SerializeEntity ( TEntity entity ) {
        return JsonSerializer.Serialize ( entity );
    }

    /// <summary>
    /// Deserializes a string into an entity.
    /// </summary>
    /// <param name="encodedEntity">The string to deserialize.</param>
    /// <returns>The deserialized entity.</returns>
    protected virtual TEntity DeserializeEntity ( string encodedEntity ) {
        return JsonSerializer.Deserialize<TEntity> ( encodedEntity )!;
    }

    SqliteList GetDatabaseInstance () {
        if (disposedValue)
            throw new ObjectDisposedException ( nameof ( SqliteRepository<TEntity> ) );

        if (sqliteList is null)
            sqliteList = OpenRepository ();

        return sqliteList;
    }

    /// <summary>
    /// Gets the number of entities in the repository.
    /// </summary>
    public int Count => GetDatabaseInstance ().Count;

    /// <summary>
    /// Gets a value indicating whether the repository is read-only.
    /// </summary>
    public bool IsReadOnly => GetDatabaseInstance ().IsReadOnly;

    /// <inheritdoc/>
    public TEntity this [ int index ] {
        get {
            string? serializedEntity = GetDatabaseInstance () [ index ];
            if (serializedEntity is null)
                throw new KeyNotFoundException ();

            return DeserializeEntity ( serializedEntity );

        }
        set {
            var serializedEntity = SerializeEntity ( value );
            GetDatabaseInstance () [ index ] = serializedEntity;
        }
    }

    /// <summary>
    /// Adds an entity to the repository.
    /// </summary>
    /// <param name="item">The entity to add.</param>
    public void Add ( TEntity item ) {
        string serializedEntity = SerializeEntity ( item );
        GetDatabaseInstance ().Add ( serializedEntity );
    }

    /// <summary>
    /// Removes all entities from the repository.
    /// </summary>
    public void Clear () {
        GetDatabaseInstance ().Clear ();
    }

    /// <summary>
    /// Determines whether the repository contains a specific entity.
    /// </summary>
    /// <param name="item">The entity to search for.</param>
    /// <returns>true if the repository contains the entity; otherwise, false.</returns>
    public bool Contains ( TEntity item ) {
        using var enumerator = GetEnumerator ();
        while (enumerator.MoveNext ()) {
            if (enumerator.Current.Equals ( item ))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Copies the entities in the repository to an array, starting at a particular index.
    /// </summary>
    /// <param name="array">The array to copy the entities to.</param>
    /// <param name="arrayIndex">The index in the array to start copying at.</param>
    public void CopyTo ( TEntity [] array, int arrayIndex ) {
        int index = arrayIndex;
        foreach (var item in this) {
            if (index >= array.Length)
                break;
            array [ index ] = item;
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the entities in the repository.
    /// </summary>
    /// <returns>An enumerator that iterates through the entities in the repository.</returns>
    public IEnumerator<TEntity> GetEnumerator () {
        foreach (var serializedEntity in GetDatabaseInstance ()) {
            if (serializedEntity is null)
                continue;

            yield return DeserializeEntity ( serializedEntity );
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the entities in the repository, along with their identifiers.
    /// </summary>
    /// <returns>An enumerator that iterates through the entities in the repository, along with their identifiers.</returns>
    public IEnumerable<KeyValuePair<int, TEntity>> AsIdentifiedEnumerable () {
        foreach (var row in GetDatabaseInstance ().AsIdentifiedEnumerable ()) {
            if (row.Value is null)
                continue;

            yield return new KeyValuePair<int, TEntity> ( row.Key, DeserializeEntity ( row.Value ) );
        }
    }

    /// <summary>
    /// Removes the entity at the specified index from the repository.
    /// </summary>
    /// <param name="id">The index of the entity to remove.</param>
    public void RemoveAt ( int id ) {
        GetDatabaseInstance ().RemoveAt ( id );
    }

    /// <summary>
    /// Removes the first occurrence of a specific entity from the repository.
    /// </summary>
    /// <param name="item">The entity to remove.</param>
    /// <returns>true if the entity was removed; otherwise, false.</returns>
    public bool Remove ( TEntity item ) {
        using var enumerator = AsIdentifiedEnumerable ().GetEnumerator ();
        while (enumerator.MoveNext ()) {
            if (enumerator.Current.Value.Equals ( item )) {
                RemoveAt ( enumerator.Current.Key );
                return true;
            }
        }
        return false;
    }

    IEnumerator IEnumerable.GetEnumerator () {
        return GetEnumerator ();
    }

    /// <summary>
    /// Releases unmanaged and, optionally, managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose ( bool disposing ) {
        if (!disposedValue) {
            if (disposing) {
                sqliteList?.Dispose ();
            }

            disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose () {
        Dispose ( disposing: true );
        GC.SuppressFinalize ( this );
    }

    /// <inheritdoc/>
    public int IndexOf ( TEntity item ) {
        using var enumerator = AsIdentifiedEnumerable ().GetEnumerator ();
        while (enumerator.MoveNext ()) {
            if (enumerator.Current.Value.Equals ( item ))
                return enumerator.Current.Key;
        }
        return -1;
    }

    void IList<TEntity>.Insert ( int index, TEntity item ) {
        throw new NotSupportedException ();
    }
}
