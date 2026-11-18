using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
namespace WoWProbe
{
    public sealed class QuickRouteEditor:UserControl
    {
        readonly TextBox routeName=new TextBox{Text="我的打怪路线",Width=150},mapId=new TextBox{Width=75},zone=new TextBox{Width=120};
        readonly NumericUpDown mode=N(1,3,1,0),corridor=N(.05m,2,.5m,2),tolerance=N(.01m,.5m,.1m,2),axis=N(.1m,10,1.5m,3),offset=N(-6.2832m,6.2832m,0,4),turn=N(.1m,10,5.85m,3),speed=N(.0001m,.2m,.008m,4);
        readonly NumericUpDown chase=N(.05m,5,2,2);
        readonly TextBox openingKeys=new TextBox{Text="242223",Width=160},loopKeys=new TextBox{Text="2",Width=160};
        readonly NumericUpDown skillSeconds=N(.1m,5,.75m,2);
        readonly CheckBox vendorEnabled=new CheckBox{Text="背包满后自动前往售卖NPC并返回",AutoSize=true};readonly NumericUpDown vendorX=N(0,100,0,3),vendorY=N(0,100,0,3);readonly TextBox vendorName=new TextBox{Width=170};
        readonly CheckBox repairEnabled=new CheckBox{Text="装备红了自动前往修理NPC并返回",AutoSize=true};readonly NumericUpDown repairX=N(0,100,0,3),repairY=N(0,100,0,3);readonly TextBox repairName=new TextBox{Width=170};
        readonly NumericUpDown restBelow=N(30,95,80,0);readonly TextBox drinkKey=new TextBox{Text="",MaxLength=1,Width=70};
        readonly ComboBox sign=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=85};
        readonly CheckBox confirmed=new CheckBox{Text="本机地图与移动参数已核对",AutoSize=true};
        readonly DataGridView grid=new DataGridView();readonly RouteCanvas canvas=new RouteCanvas{Dock=DockStyle.Fill};
        readonly Label info=new Label{AutoSize=true};
        readonly Func<FarmObservation> readCurrent;
        readonly string recordingPath;
        readonly TabControl tabs=new TabControl{Dock=DockStyle.Fill};
        readonly QuickServiceRoutePanel vendorRoute,repairRoute;
        readonly QuickServiceRoutePanel corpseRoute;QuickVendorSettings corpseSettings=new QuickVendorSettings();
        string CorpsePath {get{return Path.Combine(Path.GetDirectoryName(recordingPath),"quick-corpse-route.json");}}
        public event Action ServiceRecordingChanged;
        public bool RecordingServiceRoute {get{return vendorRoute!=null&&vendorRoute.IsRecording||repairRoute!=null&&repairRoute.IsRecording||corpseRoute!=null&&corpseRoute.IsRecording;}}
        public QuickRouteEditor()
            :this(null,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol-recording.json")){}
        public QuickRouteEditor(Func<FarmObservation> reader,string path)
        {
            readCurrent=reader;recordingPath=path;
            Dock=DockStyle.Fill;
            vendorRoute=CreateServicePanel(false);repairRoute=CreateServicePanel(true);
            corpseRoute=new QuickServiceRoutePanel(()=>ReadCurrent(true),()=>readCurrent!=null?readCurrent():GrindCli.ReadTelemetry(),ReadEditor,()=>corpseSettings.Clone(),s=>{corpseSettings=s.Clone();corpseRoute.Bind(s);},SaveCorpseRoute,UpdateRecordingState,()=>vendorRoute.IsRecording||repairRoute.IsRecording,readCurrent==null,true);
            Controls.Add(tabs);
            var routeTab=new TabPage("固定巡航点");var settingsTab=new TabPage("本机地图与移动参数");tabs.TabPages.Add(routeTab);tabs.TabPages.Add(settingsTab);
            var skillTab=new TabPage("技能设置");tabs.TabPages.Add(skillTab);var skillPanel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10)};skillTab.Controls.Add(skillPanel);
            tabs.TabPages.Add(ServiceTab(false));
            var restTab=new TabPage("恢复设置");tabs.TabPages.Add(restTab);var restPanel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10)};restTab.Controls.Add(restPanel);var restRow=Row();restRow.AutoSize=true;L(restRow,"脱战血量/适用法力低于（%）");restRow.Controls.Add(restBelow);L(restRow,"饮水键（法力职业填写）");restRow.Controls.Add(drinkKey);restPanel.Controls.Add(restRow);restPanel.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(780,0),Text="自动吃喝已启用。把食物放在主动作条第12格（=键）。\n脱战低于阈值就吃喝，恢复至99%后再寻怪；启动、复活后先恢复满。\n盗贼使用能量，只吃食物补血，不会按饮水键。法力职业请把饮水物品放在上方填写的键。\n吃喝期间受到攻击优先反击，脱战后重新恢复。食物耗尽或60秒仍未恢复会停止并提示。"});
            tabs.TabPages.Add(ServiceTab(true));
            var corpseTab=new TabPage("跑尸路线");var corpsePanel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10),AutoScroll=true};corpseTab.Controls.Add(corpsePanel);corpsePanel.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(760,0),Text="在墓地点击开始录制，绕过障碍走到打怪区后点击结束录制即可保存；存活或灵魂状态均可，中途复活不影响录制。\n录制期间不会自动移动。死亡后沿此路线跑尸，不与灵魂医者复活。路线单独保存在 quick-corpse-route.json。"});corpsePanel.Controls.Add(corpseRoute);tabs.TabPages.Add(corpseTab);
            if(File.Exists(CorpsePath)){try{var r=RouteStore.Load(CorpsePath);if(r.RouteType=="corpse_run"){corpseSettings=new QuickVendorSettings{Enabled=true,RouteComplete=true,Points=r.Points,X=r.Points[r.Points.Count-1].X,Y=r.Points[r.Points.Count-1].Y};corpseRoute.Bind(corpseSettings);}}catch(Exception ex){corpsePanel.Controls.Add(new Label{AutoSize=true,Text="旧跑尸路线未载入："+ex.Message});}}
            var sk1=Row();sk1.AutoSize=true;L(sk1,"每只怪开场一次（可留空）");sk1.Controls.Add(openingKeys);skillPanel.Controls.Add(sk1);var sk2=Row();sk2.AutoSize=true;L(sk2,"之后重复循环");sk2.Controls.Add(loopKeys);L(sk2,"技能间隔（秒）");sk2.Controls.Add(skillSeconds);skillPanel.Controls.Add(sk2);skillPanel.Controls.Add(new Label{AutoSize=true,Text="例如循环填242223，间隔填0.75，就每0.75秒尝试下一键并循环。\n开场填242223、循环填2，则每只怪先打242223，之后持续2。\n支持0–9、-、=键；新怪重新从开场开始。发送尝试不等于技能施放成功。"});
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1};root.RowStyles.Add(new RowStyle(SizeType.Absolute,34));root.RowStyles.Add(new RowStyle(SizeType.Absolute,35));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,32));routeTab.Controls.Add(root);
            var names=Row();root.Controls.Add(names,0,0);L(names,"名称");names.Controls.Add(routeName);L(names,"允许偏离（坐标点）");names.Controls.Add(corridor);L(names,"到点误差");names.Controls.Add(tolerance);
            var buttons=Row();root.Controls.Add(buttons,0,1);buttons.AutoScroll=true;buttons.WrapContents=false;buttons.Controls.Add(B("新建巡航路线",NewRoute));buttons.Controls.Add(B("保存当前巡航点",CapturePoint));buttons.Controls.Add(B("闭合到起点",CloseLoop));buttons.Controls.Add(B("删除所选点",Delete));buttons.Controls.Add(B("载入方案",LoadPlanFile));buttons.Controls.Add(B("保存方案",Save));
            var center=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=1,ColumnCount=2};center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));root.Controls.Add(center,0,2);
            grid.Dock=DockStyle.Fill;grid.RowHeadersVisible=false;grid.AllowUserToAddRows=true;grid.AllowUserToDeleteRows=true;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.BackgroundColor=Color.White;
            grid.Columns.Add("Point","点");grid.Columns[0].ReadOnly=true;grid.Columns[0].FillWeight=40;grid.Columns.Add("X","X（0–100）");grid.Columns.Add("Y","Y（0–100）");center.Controls.Add(grid,0,0);center.Controls.Add(canvas,1,0);
            info.Text="手填坐标或逐点追加；例如A→B→C→D→A。路点之间请沿可通行道路设置。";root.Controls.Add(info,0,3);
            var settings=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8)};settingsTab.Controls.Add(settings);
            var map=Row();map.AutoSize=true;L(map,"地图ID");map.Controls.Add(mapId);L(map,"模式");map.Controls.Add(mode);L(map,"区域");map.Controls.Add(zone);map.Controls.Add(B("读取当前地图",ReadMap));settings.Controls.Add(map);
            var first=Row();first.AutoSize=true;L(first,"横纵比");first.Controls.Add(axis);L(first,"朝向方向");sign.Items.AddRange(new object[]{"-1","+1"});sign.SelectedIndex=0;first.Controls.Add(sign);L(first,"朝向偏移");first.Controls.Add(offset);settings.Controls.Add(first);
            var second=Row();second.AutoSize=true;L(second,"转速 rad/s");second.Controls.Add(turn);L(second,"移动速度");second.Controls.Add(speed);second.Controls.Add(B("导入标定",ImportCalibration));settings.Controls.Add(second);settings.Controls.Add(confirmed);
            var combat=Row();combat.AutoSize=true;L(combat,"离线追怪范围（坐标点）");combat.Controls.Add(chase);settings.Controls.Add(combat);
            settings.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(800,0),Text="坐标使用地图显示的0–100。新电脑需核对插件、地图、方向键与移动参数。\n巡航沿输入顺序循环；发现附近黄名怪可离开路线接近、击杀，之后返回原路段。\n主动接近最多12秒且受追怪范围限制。受到攻击优先还击，脱战后再恢复、返回巡航；路段间请避开障碍。"});
            grid.CellEndEdit+=delegate{Preview();};grid.RowsRemoved+=delegate{Preview();};corridor.ValueChanged+=delegate{Preview();};tolerance.ValueChanged+=delegate{Preview();};
            foreach(var number in new[]{axis,offset,turn,speed,mode})number.ValueChanged+=delegate{confirmed.Checked=false;Preview();};sign.SelectedIndexChanged+=delegate{confirmed.Checked=false;};mapId.TextChanged+=delegate{confirmed.Checked=false;};zone.TextChanged+=delegate{confirmed.Checked=false;};
        }
        static NumericUpDown N(decimal min,decimal max,decimal value,int digits){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,DecimalPlaces=digits,Increment=digits>2?.0001m:digits==0?1:.01m,Width=90};}
        static FlowLayoutPanel Row(){return new FlowLayoutPanel{AutoSize=false,Height=32,Dock=DockStyle.Top,WrapContents=false};}
        static void L(Control parent,string s){parent.Controls.Add(new Label{Text=s,AutoSize=true,Margin=new Padding(5,7,3,0)});}
        static Button B(string s,Action run){var b=new Button{Text=s,AutoSize=true,Margin=new Padding(2)};b.Click+=delegate{try{run();}catch(Exception e){MessageBox.Show(e.Message,"未执行",MessageBoxButtons.OK,MessageBoxIcon.Information);}};return b;}
        QuickVendorSettings ServiceSettings(bool repair)
        {var s=new QuickVendorSettings{Enabled=(repair?repairEnabled:vendorEnabled).Checked,X=(double)(repair?repairX:vendorX).Value/100,Y=(double)(repair?repairY:vendorY).Value/100,Name=(repair?repairName:vendorName).Text.Trim()};(repair?repairRoute:vendorRoute).ApplyTo(s);return s;}
        void ApplyService(bool repair,QuickVendorSettings s)
        {(repair?repairEnabled:vendorEnabled).Checked=s.Enabled;(repair?repairX:vendorX).Value=(decimal)s.X*100;(repair?repairY:vendorY).Value=(decimal)s.Y*100;(repair?repairName:vendorName).Text=s.Name;(repair?repairRoute:vendorRoute).Bind(s);}
        QuickServiceRoutePanel CreateServicePanel(bool repair)
        {return new QuickServiceRoutePanel(Current,()=>readCurrent!=null?readCurrent():GrindCli.ReadTelemetry(),ReadEditor,()=>ServiceSettings(repair),s=>{ApplyService(repair,s);if(s.RouteComplete)ApplyService(!repair,s.Clone());},()=>QuickRoutePlan.SaveRecording(recordingPath,ReadEditor()),UpdateRecordingState,()=>repair?vendorRoute!=null&&vendorRoute.IsRecording:repairRoute!=null&&repairRoute.IsRecording,readCurrent==null);}
        void UpdateRecordingState()
        {
            bool active=RecordingServiceRoute;foreach(TabPage page in tabs.TabPages)page.Enabled=!active||page.Text=="自动售卖"&&vendorRoute.IsRecording||page.Text=="自动修理"&&repairRoute.IsRecording||page.Text=="跑尸路线"&&corpseRoute.IsRecording;
            vendorEnabled.Enabled=repairEnabled.Enabled=!active;
            if(!active)Preview();if(ServiceRecordingChanged!=null)ServiceRecordingChanged();
        }
        TabPage ServiceTab(bool repair)
        {
            var page=new TabPage(repair?"自动修理":"自动售卖");var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8)};page.Controls.Add(panel);panel.Controls.Add(repair?repairEnabled:vendorEnabled);
            var row=Row();row.AutoSize=true;L(row,"NPC终点X");var x=repair?repairX:vendorX;var y=repair?repairY:vendorY;x.Enabled=y.Enabled=false;row.Controls.Add(x);L(row,"Y");row.Controls.Add(y);L(row,"NPC名称（可选）");row.Controls.Add(repair?repairName:vendorName);panel.Controls.Add(row);panel.Controls.Add(repair?repairRoute:vendorRoute);
            panel.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(760,0),Text="售卖和维修共用NPC路线，录制一次即可，结束时自动同步两页。\n可从当前地图任意位置开始，绕过障碍走到NPC旁结束。包满后先前往录制起点，再沿整条路线去NPC。\n到NPC旁按F打开商店，等待客户端自动售卖和修理完成，沿原路返回。"+(repair?"\n读到装备实际耐久为0后触发修理，确认耐久恢复满。":"\n包满脱战后触发售卖，仍需客户端已有自动清包功能，以实际空位增加确认。")});return page;
        }
        public void EnableAutoVendor(){vendorEnabled.Checked=true;}
        public void EnableAutoRepair(){repairEnabled.Checked=true;}
        public QuickRoutePlan ReadPlan(bool live)
        {if(RecordingServiceRoute)throw new InvalidOperationException("请先结束NPC路线录制，再开始打怪或执行售卖修理。");var p=ReadEditor();p.Validate(live);return p;}
        QuickRoutePlan ReadEditor()
        {
            grid.EndEdit();uint id;if(!UInt32.TryParse(mapId.Text,out id)||id==0)throw new FormatException("请先读取或输入地图ID。");
            var r=new RouteDocument{RouteId="manual-farm",Name=routeName.Text.Trim(),RouteType="farm",Loop=true,MapId=id,MapMode=(int)mode.Value,Zone=zone.Text.Trim()};
            foreach(DataGridViewRow row in grid.Rows)
            {
                if(row.IsNewRow)continue;double x,y;
                if(!Double.TryParse(Convert.ToString(row.Cells[1].Value),NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!Double.TryParse(Convert.ToString(row.Cells[2].Value),NumberStyles.Float,CultureInfo.InvariantCulture,out y)||!NavigationMath.InRange(x,0,100)||!NavigationMath.InRange(y,0,100))throw new FormatException("第"+(row.Index+1)+"点必须输入0–100的X、Y坐标。");
                r.Points.Add(new Waypoint{X=x/100,Y=y/100,Tolerance=(double)tolerance.Value/100});
            }
            var p=new QuickRoutePlan{Route=r,CorridorWidth=(double)corridor.Value/100,CombatRadius=(double)chase.Value/100,OpeningKeys=QuickCombat.CleanKeys(openingKeys.Text,true),LoopKeys=QuickCombat.CleanKeys(loopKeys.Text,false),SkillIntervalMs=(int)(skillSeconds.Value*1000),RestBelowPercent=(int)restBelow.Value,DrinkKey=drinkKey.Text,Calibration=new NavigationCalibration{MapId=id,MapMode=r.MapMode,Zone=r.Zone,AxisScale=(double)axis.Value,FacingSign=sign.SelectedIndex==0?-1:1,FacingOffset=(double)offset.Value,TurnRadiansPerSecond=(double)turn.Value,ForwardUnitsPerSecond=(double)speed.Value,Confirmed=confirmed.Checked}};
            p.Vendor=ServiceSettings(false);p.Repair=ServiceSettings(true);
            if(p.Vendor.RouteComplete&&!p.Repair.RouteComplete){p.Repair=p.Vendor.Clone();}
            else if(p.Repair.RouteComplete&&!p.Vendor.RouteComplete){p.Vendor=p.Repair.Clone();}
            return p;
        }
        public void Bind(QuickRoutePlan p)
        {
            if(RecordingServiceRoute)throw new InvalidOperationException("请先结束录制，再载入其他方案。");
            vendorRoute.Bind(p.Vendor);repairRoute.Bind(p.Repair);
            routeName.Text=p.Route.Name;mapId.Text=p.Route.MapId.ToString();mode.Value=p.Route.MapMode;zone.Text=p.Route.Zone;
            corridor.Value=(decimal)p.CorridorWidth*100;if(p.Route.Points.Count>0)tolerance.Value=(decimal)p.Route.Points[0].Tolerance*100;
            chase.Value=(decimal)p.CombatRadius*100;
            openingKeys.Text=p.OpeningKeys;loopKeys.Text=p.LoopKeys;skillSeconds.Value=(decimal)p.SkillIntervalMs/1000;
            restBelow.Value=p.RestBelowPercent;drinkKey.Text=p.DrinkKey;
            vendorEnabled.Checked=p.Vendor.Enabled;vendorX.Value=(decimal)p.Vendor.X*100;vendorY.Value=(decimal)p.Vendor.Y*100;vendorName.Text=p.Vendor.Name;
            repairEnabled.Checked=p.Repair.Enabled;repairX.Value=(decimal)p.Repair.X*100;repairY.Value=(decimal)p.Repair.Y*100;repairName.Text=p.Repair.Name;
            axis.Value=(decimal)p.Calibration.AxisScale;offset.Value=(decimal)p.Calibration.FacingOffset;turn.Value=(decimal)p.Calibration.TurnRadiansPerSecond;speed.Value=(decimal)p.Calibration.ForwardUnitsPerSecond;sign.SelectedIndex=p.Calibration.FacingSign==-1?0:1;
            grid.Rows.Clear();foreach(var point in p.Route.Points)grid.Rows.Add("",(point.X*100).ToString("F3",CultureInfo.InvariantCulture),(point.Y*100).ToString("F3",CultureInfo.InvariantCulture));confirmed.Checked=false;Preview();
        }
        void Preview()
        {
            int count=0;foreach(DataGridViewRow row in grid.Rows)if(!row.IsNewRow)row.Cells[0].Value=count<26?((char)('A'+count++)).ToString():"P"+(++count);
            try{var p=ReadPlan(false);grid.Rows[grid.Rows.Count-2].Cells[0].Value="A";canvas.Route=p.Route;canvas.AxisScale=p.Calibration.AxisScale;info.Text="已配置"+(p.Route.Points.Count-1)+"个巡航点并回到A；按输入顺序循环。";}catch{canvas.Route=null;info.Text="手填0–100坐标，至少两个不同点；最后一个点回到A。";}canvas.Invalidate();
        }
        void SaveCorpseRoute(){if(!corpseSettings.RouteComplete)return;var p=ReadEditor();var r=new RouteDocument{RouteId="recorded-ghost",Name="墓地到打怪区",RouteType="corpse_run",MapId=p.Route.MapId,MapMode=p.Route.MapMode,Zone=p.Route.Zone};foreach(var w in corpseSettings.Points)r.Points.Add(w.Clone());RouteStore.Save(CorpsePath,r);}
        FarmObservation Current(){return ReadCurrent(false);}
        FarmObservation ReadCurrent(bool ghost)
        {
            FarmObservation o;
            if(readCurrent!=null)o=readCurrent();
            else
            {
                var form=FindForm();bool visible=form!=null&&form.Visible;
                try{if(visible)form.Hide();GrindCli.FocusGameWindow();GrindCli.OpenTelemetry();o=GrindCli.ReadTelemetry();}
                finally{if(visible&&!form.IsDisposed){form.Show();form.Activate();}}
            }
            if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0||(ghost?o.PlayerDead&&!o.PlayerGhost:o.PlayerDead||o.PlayerGhost)||o.InCombat||!NavigationMath.InRange(o.X,0,1)||!NavigationMath.InRange(o.Y,0,1))throw new InvalidOperationException(ghost?"请在存活或灵魂状态、脱战时录制跑尸路线。":"未读到新鲜、存活且脱战的角色坐标，未保存巡航点。");return o;
        }
        void ReadMap(){var o=Current();if(grid.Rows.Count>1&&mapId.Text!=""&&(mapId.Text!=o.MapId.ToString()||zone.Text!=o.Zone))throw new InvalidOperationException("当前地图与已有路点不同，请先另存或清空原路点。");mapId.Text=o.MapId.ToString();mode.Value=o.MapMode;zone.Text=o.Zone;}
        void NewRoute()
        {
            if(RecordingServiceRoute)throw new InvalidOperationException("请先结束当前路线录制。");
            var o=Current();QuickRoutePlan p;string archived="";
            if(grid.Rows.Count>1)
            {
                p=ReadEditor();string directory=Path.Combine(Path.GetDirectoryName(recordingPath),"patrol-routes");Directory.CreateDirectory(directory);
                archived=Path.Combine(directory,"巡航-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".json");
                QuickRoutePlan.SaveRecording(archived,p);
            }
            else p=new QuickRoutePlan();
            p.Route=new RouteDocument{RouteId="manual-farm",Name="新巡航路线 "+DateTime.Now.ToString("MM-dd HH:mm"),RouteType="farm",Loop=true,MapId=o.MapId,MapMode=o.MapMode,Zone=o.Zone};
            p.Calibration.MapId=o.MapId;p.Calibration.MapMode=o.MapMode;p.Calibration.Zone=o.Zone;p.Calibration.Confirmed=false;
            p.Vendor=new QuickVendorSettings();p.Repair=new QuickVendorSettings();
            p.Route.Points.Add(new Waypoint{X=Math.Round(o.X*100,3)/100,Y=Math.Round(o.Y*100,3)/100,Tolerance=(double)tolerance.Value/100});
            QuickRoutePlan.SaveRecording(recordingPath,p);Bind(p);
            info.Text="已新建路线并保存当前位置为A。继续走到B、C保存，最后闭合到起点。"+(archived.Length>0?"旧路线已存入patrol-routes，可载入恢复。":"");
        }
        void CapturePoint()
        {
            var o=Current();if(mapId.Text==""){mapId.Text=o.MapId.ToString();mode.Value=o.MapMode;zone.Text=o.Zone;}if(mapId.Text!=o.MapId.ToString()||(int)mode.Value!=o.MapMode||zone.Text!=o.Zone)throw new InvalidOperationException("当前地图与方案不同，不能混录。");
            var p=ReadEditor();if(p.Route.Points.Count>2){var a=p.Route.Points[0];var z=p.Route.Points[p.Route.Points.Count-1];if(a.X==z.X&&a.Y==z.Y)p.Route.Points.RemoveAt(p.Route.Points.Count-1);}
            p.Route.Points.Add(new Waypoint{X=Math.Round(o.X*100,3)/100,Y=Math.Round(o.Y*100,3)/100,Tolerance=(double)tolerance.Value/100});
            QuickRoutePlan.SaveRecording(recordingPath,p);bool wasConfirmed=confirmed.Checked;Bind(p);confirmed.Checked=wasConfirmed;info.Text="已保存巡航点"+grid.Rows[grid.Rows.Count-2].Cells[0].Value+"；走到下一处后再次点击。草稿已写入本机。";
        }
        void CloseLoop(){grid.EndEdit();if(grid.Rows.Count<3)throw new InvalidOperationException("请先输入至少两个不同点。");var a=grid.Rows[0];var b=grid.Rows[grid.Rows.Count-2];if(Convert.ToString(a.Cells[1].Value)!=Convert.ToString(b.Cells[1].Value)||Convert.ToString(a.Cells[2].Value)!=Convert.ToString(b.Cells[2].Value))grid.Rows.Add("A",a.Cells[1].Value,a.Cells[2].Value);var p=ReadPlan(false);QuickRoutePlan.SaveRecording(recordingPath,p);Preview();}
        void Delete(){foreach(DataGridViewRow r in grid.SelectedRows)if(!r.IsNewRow)grid.Rows.Remove(r);QuickRoutePlan.SaveRecording(recordingPath,ReadEditor());Preview();}
        void LoadPlanFile(){using(var d=new OpenFileDialog{Filter="巡航方案、旧路线或录点草稿|*.json"})if(d.ShowDialog()==DialogResult.OK){var p=QuickRoutePlan.Import(d.FileName);Bind(p);QuickRoutePlan.SaveRecording(recordingPath,p);}}
        void Save(){var p=ReadPlan(false);using(var d=new SaveFileDialog{Filter="巡航方案|*.json",FileName="我的巡航路线.json"})if(d.ShowDialog()==DialogResult.OK)QuickRoutePlan.Save(d.FileName,p);}
        void ImportRoute(){LoadPlanFile();}
        void ImportCalibration(){using(var d=new OpenFileDialog{Filter="地图标定|*.json"})if(d.ShowDialog()==DialogResult.OK){if(new FileInfo(d.FileName).Length>65536)throw new FormatException("标定文件过大。");var c=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<NavigationCalibration>(File.ReadAllText(d.FileName));c.Validate();var p=ReadPlan(false);if(c.MapId!=p.Route.MapId||c.MapMode!=p.Route.MapMode||c.Zone!=p.Route.Zone)throw new FormatException("标定与路线地图不同。");p.Calibration=c;Bind(p);}}
    }
}
