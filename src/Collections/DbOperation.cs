namespace CypherPotato.SqliteCollections;

abstract class DbOperation {
}

sealed class InsertEntityDbOperation : DbOperation {
    public required byte [] EntityData { get; set; }
}

sealed class UpdateEntityDbOperation : DbOperation {
    public long RowId { get; set; }
    public required byte [] EntityData { get; set; }
}

sealed class DeleteEntityDbOperation : DbOperation {
    public long RowId { get; set; }
}

sealed class ClearDbOperation : DbOperation {
}