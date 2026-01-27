using System;
using System.Linq;
using System.Text.Json;

namespace EventSourcingDb.Tests;

public static class JsonElementComparer
{
    public static bool Equals(JsonElement x, JsonElement y)
    {
        if (x.ValueKind != y.ValueKind)
        {
            return false;
        }

        switch (x.ValueKind)
        {
            case JsonValueKind.Object:
                var xProps = x.EnumerateObject().OrderBy(p => p.Name).ToList();
                var yProps = y.EnumerateObject().OrderBy(p => p.Name).ToList();
                if (xProps.Count != yProps.Count)
                    return false;
                for (int i = 0; i < xProps.Count; i++)
                {
                    if (xProps[i].Name != yProps[i].Name)
                        return false;
                    if (!Equals(xProps[i].Value, yProps[i].Value))
                        return false;
                }
                return true;
            case JsonValueKind.Array:
                var xArr = x.EnumerateArray().ToList();
                var yArr = y.EnumerateArray().ToList();
                if (xArr.Count != yArr.Count)
                    return false;
                for (int i = 0; i < xArr.Count; i++)
                {
                    if (!Equals(xArr[i], yArr[i]))
                        return false;
                }
                return true;
            case JsonValueKind.String:
                return x.GetString() == y.GetString();
            case JsonValueKind.Number:
                return x.GetRawText() == y.GetRawText();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return x.GetBoolean() == y.GetBoolean();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            default:
                return x.GetRawText() == y.GetRawText();
        }
    }
}
