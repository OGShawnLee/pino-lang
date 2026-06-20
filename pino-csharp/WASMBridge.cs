using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using Pino;

namespace pino_csharp;

public partial class WASMBridge {
	[JSExport]
	public static string Evaluate(string code) {
		try {
			var program = Parser.ParseProgramString(code);
			var checker = new Checker();
			checker.Check(program);
			
			var compiler = new Compiler();
			var vmFn = compiler.Compile(program);
			var evaluator = new Evaluator();
			var vm = new VM(evaluator, evaluator.Globals);
			
			using var sw = new StringWriter();
			var originalOut = Console.Out;
			Console.SetOut(sw);
			
			try {
				vm.Execute(vmFn);
			} finally {
				Console.SetOut(originalOut);
			}
			return sw.ToString();
		} catch (Exception ex) {
			return "[ERROR] " + ex.Message;
		}
	}
}
