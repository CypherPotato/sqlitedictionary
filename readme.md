# SqliteDictionary

This library provides an type called `SqliteDictionary` (no namespace), which provides an `IDictionary<string, string?>` based
on an Sqlite database. Every change made, item added, modified or removed, is immediately written in the underlying Sqlite
database.

Currently, it's only allowed to store `string` values with `string` keys in one table called `base`.

Usage:

```csharp
using (var db = SqliteDictionary.Open("app"))
{
    // add or update an item
    db["item"] = "hello";

    // add an item (will throw if already exists)
    db.Add("item2", "world");

    // removes an item
    db.Remove("item");

    // gets an item, or throw an exception if not found
    string? s = db["item2"];

    // tries to get an item
    bool exists = db.TryGetValue("item2", out string? itemValue);

    // check if an item exists
    db.ContainsKey("item");

    // iterator on db
    foreach (var kv in db)
    {
        Console.WriteLine($"{kv.Key}: {kv.Value}");
    }

    // truncate/delete the db
    db.Clear();
}
```