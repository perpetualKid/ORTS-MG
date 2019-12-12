using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CSharp;

namespace Orts.Scripting.Api
{
    public class ScriptManager
    {
        private readonly Dictionary<string, Assembly> scripts = new Dictionary<string, Assembly>();
        private static readonly CSharpCodeProvider compiler = new CSharpCodeProvider();

        static CompilerParameters GetCompilerParameters()
        {
            var cp = new CompilerParameters()
            {
                GenerateInMemory = true,
                IncludeDebugInformation = Debugger.IsAttached,
                
            };
            cp.ReferencedAssemblies.Add("System.dll");
            cp.ReferencedAssemblies.Add("System.Core.dll");
            cp.ReferencedAssemblies.Add("Orts.Common.dll");
            cp.ReferencedAssemblies.Add("Orts.Scripting.Api.dll");
            return cp;
        }

        public object Load(string path, string name)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name))
                return null;

            if (Path.GetExtension(name) != ".cs")
                name += ".cs";

            path = Path.Combine(path, name);

            string type = $"Orts.Scripting.Script.{Path.GetFileNameWithoutExtension(path)}";

            if (scripts.ContainsKey(path))
                return scripts[path].CreateInstance(type, true);

            try
            {
                var compilerResults = compiler.CompileAssemblyFromFile(GetCompilerParameters(), path);
                if (!compilerResults.Errors.HasErrors)
                {
                    Assembly script = compilerResults.CompiledAssembly;
                    scripts.Add(path, script);
                    return script.CreateInstance(type, true);
                }
                else
                {
                    StringBuilder errorString = new StringBuilder();
                    errorString.AppendFormat("Skipped script {0} with error:", path);
                    errorString.Append(Environment.NewLine);
                    foreach (CompilerError error in compilerResults.Errors)
                    {
                        errorString.AppendFormat($"   {error.ErrorText}, line: {error.Line}, column: {error.Column}");
                        errorString.Append(Environment.NewLine);
                    }
                    Trace.TraceWarning(errorString.ToString());
                    return null;
                }
            }
            catch (InvalidDataException error)
            {
                Trace.TraceWarning("Skipped script {0} with error: {1}", path, error.Message);
                return null;
            }
            catch (Exception error)
            {
                if (File.Exists(path))
                    Trace.WriteLine(new FileLoadException(path, error));
                else
                    Trace.TraceWarning("Ignored missing script file {0}", path);
                return null;
            }
        }

        public string GetStatus()
        {
            return ($"{scripts.Keys.Count:F0} scripts");
        }
    }
}
