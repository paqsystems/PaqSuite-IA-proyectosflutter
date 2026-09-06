using System.Diagnostics;
using PaqAgentInstaller.Models;
using PaqAgentInstaller.Services;

namespace PaqAgentInstaller;

public sealed class WizardForm : Form
{
    private readonly InstallerSession session = new();
    private int stepIndex;
    private readonly Label titleLabel = new();
    private readonly Label stepLabel = new();
    private readonly Panel contentPanel = new();
    private readonly Button backButton = new();
    private readonly Button nextButton = new();
    private readonly Label statusLabel = new();

    // Step 0
    private readonly Label runtimeStatusLabel = new();
    private readonly CheckBox runtimeAckCheckBox = new();

    // Step 1
    private readonly TextBox agentIdBox = new();
    private readonly TextBox clientIdBox = new();
    private readonly TextBox agentTokenBox = new();
    private readonly TextBox gatewayUrlBox = new();
    private readonly TextBox sqlServerBox = new();
    private readonly TextBox sqlPortBox = new();
    private readonly TextBox sqlDatabaseBox = new();
    private readonly TextBox sqlUserBox = new();
    private readonly TextBox sqlPasswordBox = new();
    private readonly CheckBox encryptCheckBox = new();
    private readonly CheckBox trustCertCheckBox = new();
    private readonly TextBox installDirBox = new();

    // Step 2
    private readonly Label sqlTestLabel = new();
    private readonly Label gatewayTestLabel = new();
    private readonly CheckBox gatewayOverrideCheckBox = new();

    // Step 4
    private readonly Label resultLabel = new();

    private static readonly string[] StepTitles =
    [
        "0 — Runtime (.NET 8 Desktop)",
        "1 — Credenciales",
        "2 — Pruebas SQL / Gateway",
        "3 — Instalar",
        "4 — Resultado"
    ];

    public WizardForm()
    {
        Text = "PaqAgent — Instalador";
        Width = 780;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 560);

        titleLabel.Text = "Instalación del agente PaqSuite";
        titleLabel.Font = new Font(Font.FontFamily, 12, FontStyle.Bold);
        titleLabel.Dock = DockStyle.Top;
        titleLabel.Height = 36;
        titleLabel.Padding = new Padding(12, 8, 12, 0);

        stepLabel.Dock = DockStyle.Top;
        stepLabel.Height = 28;
        stepLabel.Padding = new Padding(12, 0, 12, 0);

        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Padding = new Padding(12);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 72 };
        statusLabel.Dock = DockStyle.Top;
        statusLabel.Height = 28;
        statusLabel.Padding = new Padding(12, 4, 12, 0);

        var nav = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(12, 4, 12, 8) };
        backButton.Text = "Atrás";
        backButton.Width = 100;
        backButton.Dock = DockStyle.Left;
        backButton.Click += (_, _) => MoveStep(-1);
        nextButton.Text = "Siguiente";
        nextButton.Width = 120;
        nextButton.Dock = DockStyle.Right;
        nextButton.Click += async (_, _) => await MoveNextAsync();
        nav.Controls.Add(backButton);
        nav.Controls.Add(nextButton);

        bottom.Controls.Add(nav);
        bottom.Controls.Add(statusLabel);

        Controls.Add(contentPanel);
        Controls.Add(bottom);
        Controls.Add(stepLabel);
        Controls.Add(titleLabel);

        gatewayUrlBox.Text = InstallerDefaults.ProductionGatewayUrl;
        encryptCheckBox.Checked = true;
        trustCertCheckBox.Checked = true;
        installDirBox.Text = @"C:\PaqSystems\PaqAgent";

        ShowStep(0);
    }

    private void ShowStep(int index)
    {
        stepIndex = index;
        stepLabel.Text = StepTitles[index];
        contentPanel.Controls.Clear();
        backButton.Enabled = index > 0 && index < 4;
        nextButton.Text = index switch
        {
            0 => "Siguiente",
            1 => "Siguiente",
            2 => "Siguiente",
            3 => "Instalar",
            _ => "Cerrar"
        };

        switch (index)
        {
            case 0:
                BuildStep0();
                break;
            case 1:
                BuildStep1();
                break;
            case 2:
                BuildStep2();
                break;
            case 3:
                BuildStep3();
                break;
            default:
                BuildStep4();
                break;
        }
    }

    private void BuildStep0()
    {
        var detection = RuntimeDetector.DetectDotNet8DesktopX64();
        session.RuntimePresent = detection.IsPresent;
        runtimeStatusLabel.AutoSize = false;
        runtimeStatusLabel.Dock = DockStyle.Top;
        runtimeStatusLabel.Height = 140;

        if (detection.IsPresent)
        {
            runtimeStatusLabel.Text =
                "Estado: OK — " + detection.Message
                + Environment.NewLine
                + Environment.NewLine
                + "No hace falta descargar ni instalar el runtime."
                + Environment.NewLine
                + "Pulse Siguiente para continuar con las credenciales.";

            var recheckOk = new Button { Text = "Volver a detectar", Dock = DockStyle.Top, Height = 32 };
            recheckOk.Click += (_, _) => ShowStep(0);

            contentPanel.Controls.Add(recheckOk);
            contentPanel.Controls.Add(runtimeStatusLabel);
            runtimeAckCheckBox.Checked = false;
            statusLabel.Text = "Runtime listo. Puede continuar.";
            return;
        }

        runtimeStatusLabel.Text =
            "Estado: FALTA — " + detection.Message
            + Environment.NewLine
            + Environment.NewLine
            + "Falta .NET 8 Desktop Runtime x64."
            + Environment.NewLine
            + "La instalación del runtime puede requerir reiniciar el servidor."
            + Environment.NewLine
            + "Este instalador es self-contained: puede continuar marcando el aviso abajo.";

        var downloadButton = new Button
        {
            Text = "Descargar / instalar runtime (Microsoft)",
            Dock = DockStyle.Top,
            Height = 36
        };
        downloadButton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = InstallerDefaults.DesktopRuntimeDownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                statusLabel.Text = "No se pudo abrir el navegador: " + ex.Message;
            }
        };

        runtimeAckCheckBox.Text = "Entiendo el aviso de posible reinicio y deseo continuar sin Desktop Runtime instalado.";
        runtimeAckCheckBox.Dock = DockStyle.Top;
        runtimeAckCheckBox.AutoSize = true;
        runtimeAckCheckBox.Enabled = true;
        runtimeAckCheckBox.Checked = false;

        var recheck = new Button { Text = "Volver a detectar", Dock = DockStyle.Top, Height = 32 };
        recheck.Click += (_, _) => ShowStep(0);

        contentPanel.Controls.Add(recheck);
        contentPanel.Controls.Add(runtimeAckCheckBox);
        contentPanel.Controls.Add(downloadButton);
        contentPanel.Controls.Add(runtimeStatusLabel);
        statusLabel.Text = "Instale el runtime o marque el aviso para continuar.";
    }

    private void BuildStep1()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control, bool password = false)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            control.Dock = DockStyle.Fill;
            if (control is TextBox tb && password)
            {
                tb.UseSystemPasswordChar = true;
            }

            layout.Controls.Add(control, 1, row);
        }

        AddRow("AgentId", agentIdBox);
        AddRow("ClientId", clientIdBox);
        AddRow("AgentToken", agentTokenBox, password: true);
        AddRow("Gateway URL", gatewayUrlBox);
        AddRow("Servidor SQL", sqlServerBox);
        AddRow("Puerto SQL (opc.)", sqlPortBox);
        AddRow("Base diccionario", sqlDatabaseBox);
        AddRow("Usuario SQL", sqlUserBox);
        AddRow("Contraseña SQL", sqlPasswordBox, password: true);
        AddRow("Dir. instalación", installDirBox);

        encryptCheckBox.Text = "encrypt (TLS SQL)";
        trustCertCheckBox.Text = "trustServerCertificate";
        var adv = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        adv.Controls.Add(encryptCheckBox);
        adv.Controls.Add(trustCertCheckBox);
        AddRow("SQL avanzado", adv);

        contentPanel.Controls.Add(layout);
    }

    private void BuildStep2()
    {
        sqlTestLabel.Dock = DockStyle.Top;
        sqlTestLabel.Height = 48;
        sqlTestLabel.Text = session.SqlTestOk ? "SQL: OK" : "SQL: pendiente";
        gatewayTestLabel.Dock = DockStyle.Top;
        gatewayTestLabel.Height = 48;
        gatewayTestLabel.Text = session.GatewayTestOk ? "Gateway: OK" : "Gateway: pendiente";

        var testSql = new Button { Text = "Probar SQL", Dock = DockStyle.Top, Height = 36 };
        testSql.Click += async (_, _) =>
        {
            PullCredentialsFromUi();
            var result = await SqlConnectionTester.TestAsync(session);
            session.SqlTestOk = result.Ok;
            sqlTestLabel.Text = result.Ok ? "SQL: OK — " + result.Message : "SQL: ERROR — " + result.Message;
            statusLabel.Text = result.Message;
        };

        var testGw = new Button { Text = "Probar Gateway", Dock = DockStyle.Top, Height = 36 };
        testGw.Click += async (_, _) =>
        {
            PullCredentialsFromUi();
            var result = await GatewayReachabilityTester.TestAsync(session.GatewayUrl);
            session.GatewayTestOk = result.Ok;
            gatewayTestLabel.Text = result.Ok ? "Gateway: OK — " + result.Message : "Gateway: ERROR — " + result.Message;
            statusLabel.Text = result.Message;
        };

        gatewayOverrideCheckBox.Text = "Instalar de todos modos si el Gateway falla (el agente reintentará). Default: desmarcado.";
        gatewayOverrideCheckBox.Dock = DockStyle.Top;
        gatewayOverrideCheckBox.AutoSize = true;
        gatewayOverrideCheckBox.Checked = session.GatewayOverride;

        contentPanel.Controls.Add(gatewayOverrideCheckBox);
        contentPanel.Controls.Add(testGw);
        contentPanel.Controls.Add(testSql);
        contentPanel.Controls.Add(gatewayTestLabel);
        contentPanel.Controls.Add(sqlTestLabel);
    }

    private void BuildStep3()
    {
        var summary = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text =
                "Se copiarán los binarios desde la carpeta 'agent' junto al instalador,"
                + Environment.NewLine
                + "se escribirá appsettings.local.json y se creará el servicio Windows 'PaqAgent' (start=auto)."
                + Environment.NewLine
                + Environment.NewLine
                + "Directorio: " + (string.IsNullOrWhiteSpace(session.InstallDirectory) ? installDirBox.Text : session.InstallDirectory)
                + Environment.NewLine
                + "Requiere ejecutar el instalador como Administrador."
        };
        contentPanel.Controls.Add(summary);
    }

    private void BuildStep4()
    {
        resultLabel.Dock = DockStyle.Fill;
        resultLabel.Text = session.LastMessage;
        contentPanel.Controls.Add(resultLabel);
    }

    private void PullCredentialsFromUi()
    {
        session.AgentId = agentIdBox.Text;
        session.ClientId = clientIdBox.Text;
        session.AgentToken = agentTokenBox.Text;
        session.GatewayUrl = gatewayUrlBox.Text;
        session.SqlServer = sqlServerBox.Text;
        session.SqlDatabase = sqlDatabaseBox.Text;
        session.SqlUser = sqlUserBox.Text;
        session.SqlPassword = sqlPasswordBox.Text;
        session.SqlEncrypt = encryptCheckBox.Checked;
        session.SqlTrustServerCertificate = trustCertCheckBox.Checked;
        session.InstallDirectory = installDirBox.Text.Trim();
        session.GatewayOverride = gatewayOverrideCheckBox.Checked;
        session.RuntimeAckContinue = runtimeAckCheckBox.Checked;

        if (int.TryParse(sqlPortBox.Text.Trim(), out var port) && port > 0)
        {
            session.SqlPort = port;
        }
        else
        {
            session.SqlPort = null;
        }
    }

    private void MoveStep(int delta)
    {
        var next = stepIndex + delta;
        if (next is >= 0 and <= 4)
        {
            ShowStep(next);
        }
    }

    private async Task MoveNextAsync()
    {
        statusLabel.Text = "";
        try
        {
            if (stepIndex == 0)
            {
                session.RuntimeAckContinue = runtimeAckCheckBox.Checked;
                if (!CredentialValidator.CanProceedPastRuntime(session))
                {
                    statusLabel.Text = "Instale el runtime o marque el aviso de reinicio para continuar.";
                    return;
                }

                ShowStep(1);
                return;
            }

            if (stepIndex == 1)
            {
                PullCredentialsFromUi();
                var errors = CredentialValidator.ValidateRequired(session);
                if (errors.Count > 0)
                {
                    statusLabel.Text = string.Join(" ", errors);
                    return;
                }

                ShowStep(2);
                return;
            }

            if (stepIndex == 2)
            {
                PullCredentialsFromUi();
                if (!session.SqlTestOk)
                {
                    statusLabel.Text = "Debe probar SQL con éxito antes de continuar.";
                    return;
                }

                if (!session.GatewayTestOk && !session.GatewayOverride)
                {
                    statusLabel.Text = "Gateway no OK: pruebe de nuevo o active el override avanzado.";
                    return;
                }

                ShowStep(3);
                return;
            }

            if (stepIndex == 3)
            {
                PullCredentialsFromUi();
                await RunInstallAsync();
                ShowStep(4);
                return;
            }

            Close();
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
    }

    private Task RunInstallAsync()
    {
        var target = session.InstallDirectory;
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("Directorio de instalación vacío.");
        }

        if (AppSettingsLocalWriter.Exists(target))
        {
            var answer = MessageBox.Show(
                this,
                "Ya existe appsettings.local.json. ¿Sobrescribir?",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                session.LastMessage = "Instalación cancelada: no se sobrescribió appsettings.local.json.";
                return Task.CompletedTask;
            }
        }

        var sourceAgent = AgentFilesCopier.ResolveBundledAgentDirectory();
        AgentFilesCopier.CopyAgentFiles(sourceAgent, target);
        AppSettingsLocalWriter.Write(session, target);
        var exe = AgentFilesCopier.FindAgentExecutable(target);
        var serviceResult = WindowsServiceInstaller.InstallAndStart(exe);
        session.LastMessage = serviceResult.Ok
            ? serviceResult.Message + Environment.NewLine + "Esperando aparecer online en PaqSuite."
            : serviceResult.Message;
        if (!serviceResult.Ok)
        {
            throw new InvalidOperationException(serviceResult.Message);
        }

        return Task.CompletedTask;
    }
}
