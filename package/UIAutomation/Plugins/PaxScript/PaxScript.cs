using UnityEngine;
using System.Collections;
using PaxScript.Net;
using System;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ODDFramework;

public class PaxScripting
{

	PaxScripter script;
	bool error;

	public PaxScripting(string code)
	{

		script = new PaxScripter();
		
		foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
				script.RegisterAssembly(assembly);

		script.AddModule("1");
		script.AddCode("1", code);
		script.OnChangeState += HandleScriptOnChangeState;
		script.OnPaxException += Script_OnPaxException;

	}

	public bool Run(string method, out object result)
	{

		try
		{
			error = false;
			result = script.Invoke(RunMode.Run, null, method);

			if (!error)
			{
                Debug.Log($"PaxScripting Succeeded [{method}]: {result}");
				return true;
			}

		}
		catch (Exception e)
		{
			Debug.LogError(e);
		}

		result = null;
		return false;

	}


	public static List<Method> GetMethods(string input)
	{

		string pattern = @"^\s*(?<!(?://|/\*)\s*)    # ignore commented lines
                (?:(?<access>public|private|protected|internal)\s+)?     # access modifiers (optional)
                (?:(?<static>static)\s+)?    # static keyword (optional)
                (?:(?<class>class)\s+)?     # class (optional)
                (?:(?<type>[^: ]+)\s+)?     # type (optional)
                (?<name>\w+)                # class/method name
                (?:
                    (?<params>\([^)]*\))|                    # method parameters
                    (?:\s*:\s*(?<inherits>(?:\w|[ ,]*)+))?     # inherits/implements
                )
                \s*        # match any trailing space
                $";

		Regex codeRx = new Regex(pattern, RegexOptions.IgnorePatternWhitespace);

		List<Method> methods = new List<Method>();

		string className = string.Empty;

		foreach (string line in Regex.Split(input, "\n"))
		{

			var match = codeRx.Match(line);


			if (match.Success)
			{

				if (match.Groups["class"].Value.Equals("class"))
					className = match.Groups["name"].Value;
				else if (!string.IsNullOrEmpty(className))
					methods.Add(new Method(match.Groups["name"].Value, className, match.Groups["access"].Value));

			}


		}

		return methods;

	}

	void Script_OnPaxException(PaxScripter sender, Exception e)
	{
		error = true;
		Debug.LogError(e.Message);
	}

	void HandleScriptOnChangeState(PaxScripter sender, ChangeStateEventArgs e)
	{

		if (sender.HasErrors)
		{

			error = true;

			StringBuilder errors = new StringBuilder();

			foreach (ScriptError error in sender.Error_List)
				errors.AppendLine($"({error.LineNumber}) '{error.Line}': {error.Message}");

			Debug.LogError(errors.ToString());

		}

	}


	public class Method
	{

		public string ClassName { get; private set; }

		public string Name { get; private set; }
		public string Access { get; private set; }

		public Method(string name, string className, string access)
		{
			this.ClassName = className;
			this.Name = name;
			this.Access = access;
		}

		public override string ToString()
		{
			return $"{Access} {ClassName}.{Name}";
		}
	}

}
