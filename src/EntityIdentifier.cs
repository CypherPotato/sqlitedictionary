using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace CypherPotato.SqliteCollections;

/// <summary>
/// Represents a unique identifier for an entity of type <typeparamref name="TEntity"/>.
/// </summary>
/// <typeparam name="TEntity">The type of the entity.</typeparam>
public readonly struct EntityIdentifier<TEntity> {

    /// <summary>
    /// Gets the unique identifier of the entity.
    /// </summary>
    public long Id { get; }

    /// <summary>
    /// Gets the entity associated with the identifier.
    /// </summary>
    public TEntity Entity { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityIdentifier{TEntity}"/> struct.
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    /// <param name="entity">The entity associated with the identifier.</param>
    public EntityIdentifier ( long id, TEntity entity ) {
        Id = id;
        Entity = entity;
    }

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString () {
        return string.Create ( null, stackalloc char [ 256 ], $"[{Id}, {Entity}]" );
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode () {
        return HashCode.Combine ( Id, Entity );
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns><see langword="true"/> if the specified object is equal to the current object; otherwise, <see langword="false"/>.</returns>
    public override bool Equals ( [NotNullWhen ( true )] object? obj ) {
        if (obj is EntityIdentifier<TEntity> identifier) {
            return identifier.GetHashCode () == GetHashCode ();
        }
        return false;
    }

    /// <summary>
    /// Deconstructs the current object into its constituent parts.
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    /// <param name="value">The entity associated with the identifier.</param>
    [EditorBrowsable ( EditorBrowsableState.Never )]
    public void Deconstruct ( out long id, out TEntity value ) {
        id = Id;
        value = Entity;
    }
}