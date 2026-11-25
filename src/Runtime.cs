using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Goose.Monsterpatch.BottleSwapper
{
    // No game/Unity assembly references: keep BepInEx preloader safe.
    public static class Runtime
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        const int FirstBottle = 108, LastBottle = 125, BottlePage = 6;
        static object owner, swapButton, selectedMon;
        static int selectedSlot = -1, phase; // 0 idle, 1 confirmation, 2 picker, 3 transitioning
        static object dialogue;
        static readonly List<KeyValuePair<object, bool>> tabStates = new List<KeyValuePair<object, bool>>();
        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static void Log(string s) => Console.WriteLine("[BottleSwapper] " + s);
        static Type T(string name)
        {
            Type t;
            if (types.TryGetValue(name, out t)) return t;
            t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(x => x != null);
            if (t == null) throw new TypeLoadException(name);
            types[name] = t; return t;
        }
        static Type Of(object o) => o as Type ?? o.GetType();
        static object Target(object o) => o is Type ? null : o;
        static FieldInfo Field(object o, string n) => Of(o).GetField(n, Flags) ?? throw new MissingFieldException(Of(o).FullName, n);
        static object F(object o, string n) => Field(o,n).GetValue(Target(o));
        static void SF(object o, string n, object v) => Field(o,n).SetValue(Target(o),v);
        static PropertyInfo Prop(object o, string n) => Of(o).GetProperty(n, Flags) ?? throw new MissingMemberException(Of(o).FullName, n);
        static object P(object o, string n) => Prop(o,n).GetValue(Target(o),null);
        static void SP(object o, string n, object v) => Prop(o,n).SetValue(Target(o),v,null);
        static object Call(object o, string n, params object[] a)
        {
            var m = Of(o).GetMethods(Flags).FirstOrDefault(x => x.Name == n && !x.ContainsGenericParameters &&
                x.GetParameters().Length == a.Length && x.GetParameters().Select((p,i) => a[i] == null ? !p.ParameterType.IsValueType : p.ParameterType.IsInstanceOfType(a[i])).All(x => x));
            if (m == null) throw new MissingMethodException(Of(o).FullName,n);
            return m.Invoke(Target(o),a);
        }
        static object GO(object component) => P(component,"gameObject");
        static object Tr(object go) => P(go,"transform");
        static object Component(object go, string type) => Call(go,"GetComponent",T(type));
        static Array Components(object go,string type) => (Array)Call(go,"GetComponentsInChildren",T(type),true);
        static object At(object a,int i) => ((Array)a).GetValue(i);
        static bool Active(object go) => (bool)P(go,"activeSelf");
        static void Active(object go,bool on) => Call(go,"SetActive",on);
        static bool Alive(object go) => go != null && (bool)Call(T("UnityEngine.Object"),"op_Implicit",go);
        static object Button(object go) => Component(go,"UnityEngine.UI.Button");
        static object Selection(object g) => P(F(g,"eventSystem"),"currentSelectedGameObject");
        static void Select(object g,object go) => Call(F(g,"eventSystem"),"SetSelectedGameObject",go);
        static void Sound(object g,string s) => Call(F(g,"audioSystem"),"PlaySFX",s);
        static bool Same(object a,object b) => ReferenceEquals(a,b) || (a != null && a.Equals(b));
        static float V(object v,string n) => Convert.ToSingle(v.GetType().GetField(n).GetValue(v));
        static object Vec(string type,params object[] xyz) => Activator.CreateInstance(T(type),xyz);
        static object Shift(object v,float x,float y,float z) => Vec("UnityEngine.Vector3",V(v,"x")+x,V(v,"y")+y,V(v,"z")+z);
        static object Clone(object go,object parent) => Call(T("UnityEngine.Object"),"Instantiate",go,parent,false);
        static void SetSibling(object go,int n) => Call(Tr(go),"SetSiblingIndex",n);
        static object Label(object go) => Components(go,"TMPro.TMP_Text").Cast<object>().FirstOrDefault();
        static string Text(object go) { var l=Label(go); return l==null ? "" : Convert.ToString(P(l,"text")); }
        static bool HasClick(object button,string name)
        {
            var e=P(button,"onClick");
            for(int i=0;i<(int)Call(e,"GetPersistentEventCount");i++)
                if((string)Call(e,"GetPersistentMethodName",i)==name) return true;
            return false;
        }
        static void Link(object a,object up,object down)
        {
            var b=Button(a); var nav=P(b,"navigation");
            var t=nav.GetType();
            t.GetProperty("mode").SetValue(nav,Enum.Parse(t.GetProperty("mode").PropertyType,"Explicit"),null);
            t.GetProperty("selectOnUp").SetValue(nav,Button(up),null);
            t.GetProperty("selectOnDown").SetValue(nav,Button(down),null);
            t.GetProperty("selectOnLeft").SetValue(nav,null,null);
            t.GetProperty("selectOnRight").SetValue(nav,null,null);
            SP(b,"navigation",nav);
        }
        static void ClickHandler(object go)
        {
            var b=Button(go); var p=Prop(b,"onClick");
            var e=Activator.CreateInstance(p.PropertyType);
            p.SetValue(b,e,null);
            var add=e.GetType().GetMethods(Flags).Single(x=>x.Name=="AddListener" && x.GetParameters().Length==1);
            add.Invoke(e,new object[]{Delegate.CreateDelegate(add.GetParameters()[0].ParameterType,typeof(Runtime).GetMethod(nameof(OnClick)))});
        }
        public static void OnClick() { if(owner!=null) Begin(owner); }
        public static void OnHover<TEvent>(TEvent data)
        {
            try { if(Alive(swapButton) && Active(F(owner,"menuTeamSubMenu"))) Call(owner,"HoverSubMenuButton",swapButton); }
            catch(Exception e) { Log("Hover: "+e); }
        }
        static void HoverHandler(object go)
        {
            var type=T("UnityEngine.EventSystems.EventTrigger");
            var trigger=Call(go,"GetComponent",type) ?? Call(go,"AddComponent",type);
            var entries=(IList)P(trigger,"triggers");
            entries.Clear();
            var entryType=type.GetNestedType("Entry");
            foreach(string eventName in new[]{"Select","PointerEnter"})
            {
                var entry=Activator.CreateInstance(entryType);
                var id=entryType.GetField("eventID");
                id.SetValue(entry,Enum.Parse(id.FieldType,eventName));
                var callback=entryType.GetField("callback");
                var ev=Activator.CreateInstance(callback.FieldType);
                callback.SetValue(entry,ev);
                var add=ev.GetType().GetMethods(Flags).Single(x=>x.Name=="AddListener" && x.GetParameters().Length==1);
                var method=typeof(Runtime).GetMethod(nameof(OnHover)).MakeGenericMethod(T("UnityEngine.EventSystems.BaseEventData"));
                add.Invoke(ev,new object[]{Delegate.CreateDelegate(add.GetParameters()[0].ParameterType,method)});
                entries.Add(entry);
            }
        }

        public static void PrepareMenu(object g,int slot)
        {
            try
            {
                if(Same(owner,g) && Alive(swapButton)) return;
                var mons=(Array)F(g,"teamMon");
                if(slot<0 || slot>=mons.Length || mons.GetValue(slot)==null) return;
                var panel=F(g,"menuTeamSubMenu");
                var known=(Array)F(g,"bMonsSubMenu");
                if(known.Length<4) throw new InvalidOperationException("Party submenu has fewer than four indexed buttons.");
                var rows=new List<object>();
                for(int i=0;i<4;i++) rows.Add(known.GetValue(i));
                var send=rows[3];
                var cancel=Components(panel,"UnityEngine.UI.Button").Cast<object>().Where(b=>!rows.Any(r=>Same(r,GO(b)))).FirstOrDefault(b=>HasClick(b,"Cancel") || Text(GO(b)).Trim().Equals("Cancel",StringComparison.OrdinalIgnoreCase));
                if(cancel==null && known.Length>=5) cancel=Button(known.GetValue(4));
                if(cancel==null) throw new InvalidOperationException("Could not locate native Cancel button.");
                var cancelGo=GO(cancel);
                var parent=P(Tr(send),"parent");
                if(!Same(parent,P(Tr(cancelGo),"parent")) || rows.Any(r=>!Same(parent,P(Tr(r),"parent"))))
                    throw new InvalidOperationException("Party rows have unexpected hierarchy.");
                // Measure the real native row spacing and keep the menu's bottom edge fixed.
                var sendPos=P(Tr(send),"position"); var cancelPos=P(Tr(cancelGo),"position");
                var localSend=P(Tr(send),"localPosition"); var localCancel=P(Tr(cancelGo),"localPosition");
                float dx=V(sendPos,"x")-V(cancelPos,"x"),dy=V(sendPos,"y")-V(cancelPos,"y"),dz=V(sendPos,"z")-V(cancelPos,"z");
                float spacing=V(localSend,"y")-V(localCancel,"y");
                if(spacing<=0.01f || Math.Abs(dy)<0.00001f) throw new InvalidOperationException("Unexpected party row spacing: "+spacing);
                var root=Tr(panel);
                var size=P(root,"sizeDelta"); var pivot=P(root,"pivot"); var rootPos=P(root,"position");
                var oldPositions=rows.Select(r=>P(Tr(r),"position")).ToArray();
                object clone=null;
                try
                {
                    clone=Clone(send,parent);
                    SP(clone,"name","BottleSwapper_SwapBottle");
                    var label=Label(clone) ?? throw new InvalidOperationException("Native row text missing.");
                    var localization=Call(GO(label),"GetComponent","LocalizeStringEvent");
                    if(localization!=null && Alive(localization)) SP(localization,"enabled",false);
                    SP(label,"text","Swap Bottle");
                    ClickHandler(clone); HoverHandler(clone);
                    SetSibling(clone,(int)Call(Tr(cancelGo),"GetSiblingIndex"));
                    // Grow native panel upward rather than extending past bottom of screen.
                    SP(root,"sizeDelta",Vec("UnityEngine.Vector2",V(size,"x"),V(size,"y")+spacing));
                    float py=V(pivot,"y");
                    SP(root,"position",Shift(rootPos,dx*py,dy*py,dz*py));
                    for(int i=0;i<rows.Count;i++) SP(Tr(rows[i]),"position",Shift(oldPositions[i],dx,dy,dz));
                    SP(Tr(cancelGo),"position",cancelPos);
                    SP(Tr(clone),"position",sendPos);
                    rows.Add(clone); rows.Add(cancelGo);
                    for(int i=0;i<rows.Count;i++) Link(rows[i],rows[(i+rows.Count-1)%rows.Count],rows[(i+1)%rows.Count]);
                    owner=g; swapButton=clone;
                    Log("Native party menu expanded: Summary, Switch, Item, Send To, Swap Bottle, Cancel.");
                }
                catch
                {
                    if(clone!=null) { Active(clone,false); Call(T("UnityEngine.Object"),"Destroy",clone); }
                    SP(root,"sizeDelta",size); SP(root,"position",rootPos);
                    for(int i=0;i<oldPositions.Length;i++) SP(Tr(rows[i]),"position",oldPositions[i]);
                    SP(Tr(cancelGo),"position",cancelPos);
                    throw;
                }
            }
            catch(Exception e) { Log("Menu initialization failed: "+e); }
        }
        static void SetMenuState(object g)
        {
            var menu=g.GetType().Assembly.GetType("MenuScript",true);
            var field=Field(menu,"gameState");
            field.SetValue(null,Enum.Parse(field.FieldType,"Menu"));
            SF(g,"interacting",false);
        }
        static void Begin(object g)
        {
            if(phase!=0) return;
            try
            {
                if(!Active(F(g,"menuTeamSubMenu")) || !Active(F(g,"menuTeam"))) return;
                selectedSlot=(int)F(g,"curTeamMon");
                selectedMon=At(F(g,"teamMon"),selectedSlot);
                if(selectedMon==null) return;
                dialogue=F(g,"dialogueManager");
                if(Active(F(dialogue,"menuDialogue"))) return;
                phase=1;
                Select(g,swapButton);
                SF(g,"lastButtonObj",swapButton);
                // Native typewriter + native localized Yes/No buttons.
                Call(dialogue,"ShowDialogue",new List<string>{"Would you like to swap this mon's bottle?","#yn"},true);
                Log("Confirmation opened for party slot "+selectedSlot);
            }
            catch(Exception e) { Log("Confirmation failed: "+e); Recover(g); }
        }
        public static bool AnswerYes(object d) => Answer(d,true);
        public static bool AnswerNo(object d) => Answer(d,false);
        static bool Answer(object d,bool yes)
        {
            if(phase!=1 || !Same(dialogue,d)) return false;
            phase=3;
            try { Call(owner,"StartCoroutine",AfterAnswer(owner,d,yes)); }
            catch(Exception e) { Log("Answer failed: "+e); Recover(owner); }
            return true;
        }
        static IEnumerator AfterAnswer(object g,object d,bool yes)
        {
            yield return null;
            for(int i=0;i<120 && Active(F(d,"menuDialogue"));i++) yield return null;
            try
            {
                if(Active(F(d,"menuDialogue"))) throw new InvalidOperationException("Native dialogue did not finish closing.");
                SetMenuState(g);
                if(yes) OpenPicker(g); else ReturnToParty(g);
            }
            catch(Exception e) { Log("After answer: "+e); Recover(g); }
        }
        static bool ValidBottle(object g,int id)
        {
            var q=(int[])F(g,"inventoryQ"); var items=(Array)F(g,"itemScriptableObject");
            return id>=FirstBottle && id<=LastBottle && id<q.Length && id<items.Length && Alive(items.GetValue(id));
        }
        static void OpenPicker(object g)
        {
            var q=(int[])F(g,"inventoryQ");
            var slots=(Array)F(g,"itemSlotObj");
            int first=-1;
            for(int i=0;i<slots.Length && i<=LastBottle-FirstBottle;i++)
                if(ValidBottle(g,FirstBottle+i) && q[FirstBottle+i]>0) { first=i; break; }
            if(first<0) { Sound(g,"error"); ReturnToParty(g); return; }
            Select(g,null);
            Active(F(g,"menuTeamSubMenu"),false);
            Active(F(g,"menuTeam"),false);
            Active(F(g,"menuItems"),true);
            phase=2;
            Call(g,"HoverItemTabButton",BottlePage);
            tabStates.Clear();
            foreach(object tab in (Array)F(g,"bItemTab"))
            {
                if(!Alive(tab)) continue;
                var b=Button(tab);
                if(b==null) continue;
                tabStates.Add(new KeyValuePair<object,bool>(b,(bool)P(b,"enabled")));
                SP(b,"enabled",false);
            }
            // Native inventory hides zero quantities; additionally hide unavailable item assets.
            for(int i=0;i<slots.Length;i++)
                if(!ValidBottle(g,FirstBottle+i) || q[FirstBottle+i]<=0) Active(slots.GetValue(i),false);
            Select(g,slots.GetValue(first));
            SF(g,"lastButtonObj",slots.GetValue(first));
            Log("Opened native inventory BOTTLES page.");
        }
        public static bool BlockOtherTabs(object g,int tab) => phase==2 && Same(owner,g) && tab!=BottlePage;
        public static bool CancelPicker(object g)
        {
            if(phase!=2 || !Same(owner,g)) return false;
            try { ReturnToParty(g); Sound(g,"uiCancel"); }
            catch(Exception e) { Log("Cancel: "+e); Recover(g); }
            return true;
        }
        public static bool SelectBottle(object g,int slot)
        {
            if(phase!=2 || !Same(owner,g)) return false;
            try
            {
                if(slot<0 || slot>LastBottle-FirstBottle || (int)F(g,"curItemPage")!=BottlePage)
                { Sound(g,"error"); return true; }
                var mons=(Array)F(g,"teamMon");
                if(selectedSlot<0 || selectedSlot>=mons.Length || !Same(mons.GetValue(selectedSlot),selectedMon))
                { Log("Party target changed; aborted."); ReturnToParty(g); return true; }
                int next=FirstBottle+slot;
                var q=(int[])F(g,"inventoryQ");
                int raw=(int)F(selectedMon,"bottleId");
                int old=raw==0 ? FirstBottle : raw;
                if(!ValidBottle(g,next) || !ValidBottle(g,old) || q[next]<=0 || (next!=old && q[old]>=999))
                { Sound(g,"error"); Log("Swap rejected: unavailable bottle or old bottle stack full."); return true; }
                if(next!=old)
                {
                    // All validation before mutation. Roll back exact quantities on assignment failure.
                    int oldQ=q[old], nextQ=q[next];
                    try { q[next]=nextQ-1; SF(selectedMon,"bottleId",next); q[old]=oldQ+1; }
                    catch { q[next]=nextQ; q[old]=oldQ; SF(selectedMon,"bottleId",raw); throw; }
                    Log("Swapped party slot "+selectedSlot+": "+old+" -> "+next+"; inventory "+old+"="+q[old]+", "+next+"="+q[next]);
                }
                ReturnToParty(g);
                Sound(g,"item");
            }
            catch(Exception e) { Log("Swap failed: "+e); Recover(g); }
            return true;
        }
        static void ReturnToParty(object g)
        {
            phase=0;
            Select(g,null);
            foreach(var state in tabStates) if(Alive(state.Key)) SP(state.Key,"enabled",state.Value);
            tabStates.Clear();
            Active(F(g,"menuItemDesc"),false);
            Active(F(g,"menuItems"),false);
            Active(F(g,"menuTeam"),true);
            Active(F(g,"menuTeamSubMenu"),true);
            SetMenuState(g);
            Call(g,"RefreshTeamMenu");
            Select(g,swapButton);
            SF(g,"lastButtonObj",swapButton);
            Call(g,"HoverSubMenuButton",swapButton);
            selectedMon=null; selectedSlot=-1; dialogue=null;
        }
        static void Recover(object g)
        {
            try { ReturnToParty(g); } catch(Exception e) { phase=0; Log("Recovery failed: "+e); }
        }
    }
}
