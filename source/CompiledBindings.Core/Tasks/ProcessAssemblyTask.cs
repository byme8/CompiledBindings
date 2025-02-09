﻿#nullable enable
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace CompiledBindings;

public class ProcessAssemblyTask : Task
{
	[Required]
	public ITaskItem[] ReferenceAssemblies { get; set; }

	[Required]
	public string TargetAssembly { get; set; }

	public bool AttachDebugger { get; set; }

	public override bool Execute()
	{
		try
		{
			if (AttachDebugger)
			{
				System.Diagnostics.Debugger.Launch();
			}

			TypeInfoUtils.LoadReferences(ReferenceAssemblies.Select(a => a.ItemSpec));

			var prm = new ReaderParameters(ReadingMode.Immediate)
			{
				ReadWrite = true,
				ReadSymbols = true,
				AssemblyResolver = new TypeInfoUtils.AssemblyResolver(),
			};

			var assembly = AssemblyDefinition.ReadAssembly(TargetAssembly, prm);
			try
			{
				foreach (var type in assembly.MainModule.GetAllTypes())
				{
					foreach (var attr in type.CustomAttributes.Where(a => a.AttributeType.FullName == "System.CodeDom.Compiler.GeneratedCodeAttribute"))
					{
						if (attr.ConstructorArguments[0].Value as string == "CompiledBindings")
						{
							foreach (var method in type.Methods.Where(m => m.Name.StartsWith("InitializeBeforeConstructor") || m.Name.StartsWith("InitializeAfterConstructor")))
							{
								foreach (var ctor in type.GetConstructors().Where(c => !c.IsStatic))
								{
									var body = ctor.Body;
									var instructions = body.Instructions;
									if (method.Name.StartsWith("InitializeBeforeConstructor"))
									{
										instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
										instructions.Insert(1, Instruction.Create(OpCodes.Call, method));
									}
									else
									{
										var firstInstruction = Instruction.Create(OpCodes.Ldarg_0);
										instructions.Add(firstInstruction);
										instructions.Add(Instruction.Create(OpCodes.Call, method));
										instructions.Add(Instruction.Create(OpCodes.Ret));

										for (var index = 0; index < instructions.Count - 1; index++)
										{
											var instruction = instructions[index];
											if (instruction.OpCode == OpCodes.Ret)
											{
												instructions[index] = Instruction.Create(OpCodes.Leave, firstInstruction);
											}
										}
									}
								}
							}

							foreach (var method in type.Methods.Where(m => m.Name.StartsWith("DeinitializeAfterDestructor")))
							{
								var dtor = type.Methods.FirstOrDefault(m => m.Name == "Finalize");
								if (dtor != null)
								{
									var body = dtor.Body;
									var instructions = body.Instructions;
									var firstInstruction = Instruction.Create(OpCodes.Ldarg_0);
									instructions.Add(firstInstruction);
									instructions.Add(Instruction.Create(OpCodes.Call, method));
									instructions.Add(Instruction.Create(OpCodes.Ret));

									for (var index = 0; index < instructions.Count - 1; index++)
									{
										var instruction = instructions[index];
										if (instruction.OpCode == OpCodes.Ret)
										{
											instructions[index] = Instruction.Create(OpCodes.Leave, firstInstruction);
										}
									}
								}
							}

							type.CustomAttributes.Remove(attr);
							break;
						}
					}
				}
				assembly.Write(new WriterParameters { WriteSymbols = true });
				return true;
			}
			finally
			{
				assembly.Dispose();
			}
		}
		catch (GeneratorException ex)
		{
			Log.LogError(null, null, null, ex.File, ex.LineNumber, ex.ColumnNumber, ex.EndLineNumber, ex.EndColumnNumber, ex.Message);
			return false;
		}
		catch (Exception ex)
		{
			Log.LogError(ex.Message);
			return false;
		}
		finally
		{
			TypeInfoUtils.Cleanup();
		}
	}
}

