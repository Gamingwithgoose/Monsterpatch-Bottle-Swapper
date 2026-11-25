using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Goose.Monsterpatch.BottleSwapper
{
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => new[] { "Assembly-CSharp.dll" };
        public static void Initialize() => Console.WriteLine("[BottleSwapper] v0.1.0 beta test");
        public static void Patch(AssemblyDefinition assembly)
        {
            var module = assembly.MainModule;
            var game = module.Types.Single(t => t.Name == "GameScript");
            var dialogue = module.Types.Single(t => t.Name == "DialogueManager");
            // Resolve every required target before writing any IL.
            var setup = Find(game, "MonsSubMenu", 1);
            var cancel = Find(game, "Cancel", 0);
            var select = Find(game, "SelectItemSlot", 1);
            var tab = Find(game, "HoverItemTabButton", 1);
            var yes = Find(dialogue, "HandleYesAnswer", 0);
            var no = Find(dialogue, "HandleNoAnswer", 0);
            Find(game, "HoverSubMenuButton", 1);
            Find(game, "RefreshTeamMenu", 0);
            Find(game, "HoverItemTabButton", 1);
            Find(game, "HoverItemSlot", 1);
            Prefix(module, setup, "PrepareMenu", false, true);
            Prefix(module, cancel, "CancelPicker", true, false);
            Prefix(module, select, "SelectBottle", true, true);
            Prefix(module, tab, "BlockOtherTabs", true, true);
            Prefix(module, yes, "AnswerYes", true, false);
            Prefix(module, no, "AnswerNo", true, false);
            Console.WriteLine("[BottleSwapper] Installed six native menu hooks.");
        }

        private static MethodDefinition Find(TypeDefinition t, string name, int args)
        {
            return t.Methods.Single(m => m.Name == name && m.Parameters.Count == args && m.HasBody && !m.IsStatic && m.ReturnType.FullName == "System.Void");
        }

        private static void Prefix(ModuleDefinition module, MethodDefinition target, string name, bool handled, bool hasArg)
        {
            var helper = module.ImportReference(typeof(Runtime).GetMethod(name));
            var il = target.Body.GetILProcessor();
            var original = target.Body.Instructions[0];
            il.InsertBefore(original, Instruction.Create(OpCodes.Ldarg_0));
            if (hasArg) il.InsertBefore(original, Instruction.Create(OpCodes.Ldarg_1));
            il.InsertBefore(original, Instruction.Create(OpCodes.Call, helper));
            if (handled)
            {
                il.InsertBefore(original, Instruction.Create(OpCodes.Brfalse, original));
                il.InsertBefore(original, Instruction.Create(OpCodes.Ret));
            }
            target.Body.MaxStackSize = Math.Max(target.Body.MaxStackSize, 2);
        }
    }
}
