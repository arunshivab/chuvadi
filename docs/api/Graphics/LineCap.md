# LineCap

**Enum** in `Chuvadi.Pdf.Graphics` (Graphics)

Specifies the shape of the ends of open subpaths when stroked. PDF 32000-1:2008 §8.4.3.3 — Line cap style, Table 54.

```csharp
public enum LineCap
```

## Values

| Name | Description |
|---|---|
| `Butt` | Butt cap. The stroke ends exactly at the endpoint with no extension. PDF value 0. |
| `Round` | Round cap. A semicircle of diameter equal to line width is drawn beyond the endpoint. PDF value 1. |
| `Square` | Projecting square cap. The stroke extends half the line width beyond the endpoint in a square shape. PDF value 2. |

---

_Source: [`src/Chuvadi.Pdf.Graphics/LineCap.cs`](../../../src/Chuvadi.Pdf.Graphics/LineCap.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
