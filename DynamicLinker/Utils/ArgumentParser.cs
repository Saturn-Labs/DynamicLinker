namespace DynamicLinker.Utils;

public static class ArgumentParser
{
    private static Dictionary<string, string> Parse(string[] args)
    {
        Dictionary<string, string> arguments = [];
        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            if (arg.StartsWith('/')) {
                string kv = arg[1..];
                string[] parts = kv.Split('=');
                if (parts.Length == 2) {
                    arguments[parts[0].ToLower()] = parts[1];
                }
                else {
                    arguments[kv.ToLower()] = "true";
                }
            }
        }
        return arguments;
    }

    public static T ParseInto<T>(string[] args) where T : new() {
        T t = new();
        Dictionary<string, string> arguments = Parse(args);
        foreach (var prop in typeof(T).GetProperties()) {
            if (arguments.ContainsKey(prop.Name.ToLower())) {
                prop.SetValue(t, Convert.ChangeType(arguments[prop.Name.ToLower()], prop.PropertyType));
            }
        }
        return t;
    }
}