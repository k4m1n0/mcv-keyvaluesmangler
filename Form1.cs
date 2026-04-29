using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1 : Form
{
    private List<WeaponData> weapons = null!;
    private WeaponData? currentWeaponLeft = null;
    private WeaponData? currentWeaponRight = null;

    #nullable disable

    private bool updatingControls = false;
    private bool isDirty = false;

    private const double SliderMin = 0.0;
    private const double SliderMax = 5.0;
    private const double SliderStep = 0.01;
    private const double DistanceDivisor = 9.525;

    private string lastScriptsDir = "";
    private bool refreshing = false;

    private PanelRenderer spreadRenderer = null!;
    private PanelRenderer recoilRenderer = null!;

    public Form1()
    {
        try
        {
            this.Text = "Keyvalues Mangler™ 5000";
            this.Size = new Size(1366, 768);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            string csvPath = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
            weapons = File.Exists(csvPath) ? CsvService.LoadWeapons(csvPath) : new List<WeaponData>();

            InitLeftPanel(weapons);
            InitRightPanel(weapons);
            InitCenterPanels();
            InitC64Labels();
            InitTopButtons();

            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.FormClosing += Form1_FormClosing;

            this.Shown += (s, e) =>
            {
                if (weapons.Count > 0)
                {
                    cmbWeaponsL.DataSource = null;
                    cmbWeaponsL.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsL.DisplayMember = "PrintName";
                    cmbWeaponsL.SelectedIndex = 0;

                    cmbWeaponsR.DataSource = null;
                    cmbWeaponsR.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsR.DisplayMember = "PrintName";
                    cmbWeaponsR.SelectedIndex = 0;

                    UpdateC64Labels(true);
                }
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Launch failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    private void InitCenterPanels()
    {
        int cx = 525;
        pnlSpread = new Panel { Location = new Point(cx, 38), Size = new Size(300, 300), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        EnableDoubleBuffering(pnlSpread);
        pnlSpread.Paint += PnlSpread_Paint;
        this.Controls.Add(pnlSpread);

        pnlRecoil = new Panel { Location = new Point(cx, 313), Size = new Size(300, 300), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        EnableDoubleBuffering(pnlRecoil);
        pnlRecoil.Paint += PnlRecoil_Paint;
        this.Controls.Add(pnlRecoil);

        spreadRenderer = new PanelRenderer(pnlSpread);
        recoilRenderer = new PanelRenderer(pnlRecoil);
    }

    private void InitTopButtons()
    {
        int cx = 525;
        btnSave = new Button { Text = "Save", Location = new Point(cx, 6), Size = new Size(60, 26) };
        btnSave.Click += BtnSave_Click;
        this.Controls.Add(btnSave);

        btnCsvToScripts = new Button { Text = "CSV>Scripts", Location = new Point(cx + 62, 6), Size = new Size(84, 26) };
        btnCsvToScripts.Click += BtnCsvToScripts_Click;
        this.Controls.Add(btnCsvToScripts);

        btnScriptsToCsv = new Button { Text = "Scripts>CSV", Location = new Point(cx + 152, 6), Size = new Size(84, 26) };
        btnScriptsToCsv.Click += BtnScriptsToCsv_Click;
        this.Controls.Add(btnScriptsToCsv);

        var btnRefresh = new Button { Text = "Rfsh", Location = new Point(cx + 240, 6), Size = new Size(60, 26) };
        btnRefresh.Click += BtnRefresh_Click;
        this.Controls.Add(btnRefresh);

        var btnCopy = new Button { Text = "L>R", Location = new Point(cx, 620), Size = new Size(60, 24) };
        btnCopy.Click += CopyLeftToRight;
        this.Controls.Add(btnCopy);

        var btnCopyR = new Button { Text = "R>L", Location = new Point(cx + 240, 620), Size = new Size(60, 24) };
        btnCopyR.Click += CopyRightToLeft;
        this.Controls.Add(btnCopyR);
    }

    #nullable enable
    private void CopyLeftToRight(object? sender, EventArgs e)
    {
        if (currentWeaponLeft != null && currentWeaponRight != null)
        {
            SaveControlsToWeapon(currentWeaponLeft, true);
            // Copy left weapon data into right weapon data object
            CopyWeaponDataFields(currentWeaponLeft, currentWeaponRight);
            LoadWeaponToControls(currentWeaponRight, false);
            isDirty = true;
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
        }
    }

    private void CopyRightToLeft(object? sender, EventArgs e)
    {
        if (currentWeaponRight != null && currentWeaponLeft != null)
        {
            SaveControlsToWeapon(currentWeaponRight, false);
            CopyWeaponDataFields(currentWeaponRight, currentWeaponLeft);
            LoadWeaponToControls(currentWeaponLeft, true);
            isDirty = true;
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
        }
    }

    private static void CopyWeaponDataFields(WeaponData src, WeaponData dst)
    {
        dst.DamageHeadMultiplier = src.DamageHeadMultiplier;
        dst.DamageChestMultiplier = src.DamageChestMultiplier;
        dst.DamageStomachMultiplier = src.DamageStomachMultiplier;
        dst.DamageLegMultiplier = src.DamageLegMultiplier;
        dst.DamageArmMultiplier = src.DamageArmMultiplier;
        dst.BulletSpread = src.BulletSpread;
        dst.BulletSpreadDegreesIronsighted = src.BulletSpreadDegreesIronsighted;
        dst.BulletSpreadDegreesBipod = src.BulletSpreadDegreesBipod;
        dst.BulletSpreadDegreesBipodIronsighted = src.BulletSpreadDegreesBipodIronsighted;
        dst.ViewSlideRecoilUp = src.ViewSlideRecoilUp;
        dst.ViewSlideRecoilRight = src.ViewSlideRecoilRight;
        dst.ViewSlideRecoilIronsightUp = src.ViewSlideRecoilIronsightUp;
        dst.ViewSlideRecoilIronsightRight = src.ViewSlideRecoilIronsightRight;
        dst.FireModes = src.FireModes;
        dst.FireRate = src.FireRate;
        dst.RangeModifier = src.RangeModifier;
        dst.ClipSize = src.ClipSize;
        dst.DefaultClip = src.DefaultClip;
        dst.ExtraBulletChamber = src.ExtraBulletChamber;
        dst.BulletsPerShot = src.BulletsPerShot;
        dst.IronsightSpeedScale = src.IronsightSpeedScale;
        dst.Weight = src.Weight;
        dst.ZMBuyPrice = src.ZMBuyPrice;
        dst.ZMWeight = src.ZMWeight;
        dst.MetalPenetrationDepth = src.MetalPenetrationDepth;
        dst.GlassPenetrationDepth = src.GlassPenetrationDepth;
        dst.ConcretePenetrationDepth = src.ConcretePenetrationDepth;
        dst.WoodPenetrationDepth = src.WoodPenetrationDepth;
        dst.OtherPenetrationDepth = src.OtherPenetrationDepth;
        dst.MetalDamageModifier = src.MetalDamageModifier;
        dst.GlassDamageModifier = src.GlassDamageModifier;
        dst.ConcreteDamageModifier = src.ConcreteDamageModifier;
        dst.WoodDamageModifier = src.WoodDamageModifier;
        dst.OtherDamageModifier = src.OtherDamageModifier;
        dst.CrouchSpreadMultiplier = src.CrouchSpreadMultiplier;
        dst.ProneSpreadMultiplier = src.ProneSpreadMultiplier;
        dst.StandMoveSpreadMultiplier = src.StandMoveSpreadMultiplier;
        dst.SneakMoveSpreadMultiplier = src.SneakMoveSpreadMultiplier;
        dst.CrouchMoveSpreadMultiplier = src.CrouchMoveSpreadMultiplier;
        dst.JumpSpreadMultiplier = src.JumpSpreadMultiplier;
        dst.DamageGeneric = src.DamageGeneric;
        // Copy script-relevant fields for save
        dst.FireModes = src.FireModes;
        dst.PrimaryAmmo = src.PrimaryAmmo;
    }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (isDirty)
        {
            var result = MessageBox.Show("Unsaved changes will be lost. Save now?",
                "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                BtnSave_Click(this, EventArgs.Empty);
            }
            else if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
            }
        }
    }

    private static void EnableDoubleBuffering(Control control)
    {
        typeof(Control).InvokeMember("DoubleBuffered",
            BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
            null, control, new object[] { true });
    }

    private void PnlSpread_Paint(object? sender, PaintEventArgs e)
    {
        spreadRenderer.DrawSpread(e.Graphics, currentWeaponLeft, currentWeaponRight,
            (double)nudHipSpreadL.Value, (double)nudAdsSpreadL.Value,
            (double)nudBipodHipSpreadL.Value, (double)nudBipodAdsSpreadL.Value,
            currentWeaponRight != null ? (double)nudHipSpreadR.Value : 1.0,
            currentWeaponRight != null ? (double)nudAdsSpreadR.Value : 1.0,
            currentWeaponRight != null ? (double)nudBipodHipSpreadR.Value : 0,
            currentWeaponRight != null ? (double)nudBipodAdsSpreadR.Value : 0);
    }

    private void PnlRecoil_Paint(object? sender, PaintEventArgs e)
    {
        recoilRenderer.DrawRecoil(e.Graphics, currentWeaponLeft, currentWeaponRight,
            (double)nudHipRecoilUpL.Value, (double)nudHipRecoilRightL.Value,
            (double)nudAdsRecoilUpL.Value, (double)nudAdsRecoilRightL.Value,
            currentWeaponRight != null ? (double)nudHipRecoilUpR.Value : 0,
            currentWeaponRight != null ? (double)nudHipRecoilRightR.Value : 0,
            currentWeaponRight != null ? (double)nudAdsRecoilUpR.Value : 0,
            currentWeaponRight != null ? (double)nudAdsRecoilRightR.Value : 0);
    }

    public class LogForm : Form
    {
        public LogForm(string title, string logText)
        {
            this.Text = title;
            this.Size = new Size(320, 240);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            var txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), Text = logText };
            this.Controls.Add(txt);
        }
    }
}