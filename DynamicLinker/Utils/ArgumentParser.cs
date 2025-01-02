namespace DynamicLinker.Utils;

public class ArgumentParser
{
    private readonly Dictionary<string, string> _arguments = new Dictionary<string, string>();

    public ArgumentParser(string[] args)
    {
        Parse(args);
    }

    private void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("--"))
            {
                var split = arg.Substring(2).Split('=', 2);
                if (split.Length == 2)
                {
                    _arguments[split[0]] = split[1];
                }
                else
                {
                    _arguments[split[0]] = "true"; // Handle flags like "--verbose"
                }
            }
            else if (arg.StartsWith("-"))
            {
                string key = arg.Substring(1);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    _arguments[key] = args[++i];
                }
                else
                {
                    _arguments[key] = "true"; // Handle flags like "-v"
                }
            }
        }
    }

    public string? Get(string key, string? defaultValue = null)
    {
        return _arguments.TryGetValue(key, out string? value) ? value : defaultValue;
    }

    public bool Has(string key)
    {
        return _arguments.ContainsKey(key);
    }

    public bool IsArray(string key) {
        return _arguments.TryGetValue(key, out string? value) && value.StartsWith('[') && value.EndsWith(']');
    }

    public string[] GetArray(string key) {
        if (!IsArray(key)) {
            return [];
        }
        return _arguments[key].TrimStart('[').TrimEnd(']').Split(',');
    }

    public bool Empty() {
        return _arguments.Count == 0;
    }
}