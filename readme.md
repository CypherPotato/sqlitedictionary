# SqliteDictionary

This library provides an type called `SqliteDictionary` (no namespace), which provides an `IDictionary<string, string?>` based
on an Sqlite database. Every change made, item added, modified or removed, is immediately written in the underlying Sqlite
database.

Currently, it's only allowed to store `string` values with `string` keys in one table called `base`.