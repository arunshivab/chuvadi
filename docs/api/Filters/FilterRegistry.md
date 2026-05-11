# FilterRegistry

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Central registry of PDF stream filter implementations. Call `CreateDefaultPipeline` to get a fully configured pipeline. PDF 32000-1:2008 §7.4.

```csharp
public static class FilterRegistry
```

## Methods

### `CreateDefaultPipeline`

__static__

```csharp
static FilterPipeline CreateDefaultPipeline()
```

Creates a `FilterPipeline` with all Phase 1 filters and aliases pre-registered.

### `ResolveAlias`

__static__

```csharp
static string ResolveAlias(string nameOrAlias)
```

Returns the canonical filter name for a given name or alias.

---

_Source: [`src/Chuvadi.Pdf.Filters/FilterRegistry.cs`](../../../src/Chuvadi.Pdf.Filters/FilterRegistry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
