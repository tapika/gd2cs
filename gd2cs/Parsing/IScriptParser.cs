using gd2cs.Model;

namespace gd2cs.Parsing;

public interface IScriptParser
{
    ScriptModel Parse(string source);
}
