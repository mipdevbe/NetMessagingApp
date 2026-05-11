Imports NetWinMessaging

Public Class Form1
    Private netMessaging As New Automation

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Initialize the form and controls

        Text = "Net Messaging Demo - " & Me.Handle.ToString("X")
        btnStop.Enabled = False
        AddHandler netMessaging.MessageReceived, AddressOf OnMessageReceived
    End Sub

    Private Sub btnStart_Click(sender As Object, e As EventArgs) Handles btnStart.Click
        netMessaging.Identifier = Me.txtConnectionIdentifier.Text
        If (netMessaging.Connect(Me.txtConnectionIdentifier.Text)) Then
            ' Connection successful
            btnStart.Enabled = False
            btnStart.Text = "Connected"
            btnStop.Enabled = True
        End If
    End Sub

    Private Sub btnStop_Click(sender As Object, e As EventArgs) Handles btnStop.Click
        If (netMessaging.Disconnect()) Then
            ' Disconnection successful
            btnStart.Enabled = True
            btnStart.Text = "Connect"
            btnStop.Enabled = False
        End If
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        netMessaging.SendToIdentifier(txtTargetIdentifier.Text, $"{txtMessage.Text()}")
    End Sub

    Private Sub OnMessageReceived(sender As Object, e As MessageReceivedEventArgs)
        If Me.InvokeRequired Then
            Me.BeginInvoke(Sub() HandleIncomingMessage(e))
        Else
            HandleIncomingMessage(e)
        End If
    End Sub

    Private Sub HandleIncomingMessage(e As MessageReceivedEventArgs)
        ' Application-specific processing
        lstMessages.Items.Add($"{DateTime.Now.ToString()} - {e.SenderHwnd.ToString("X")} : {e.Message}")
    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        btnStop_Click(Me, New EventArgs()) ' Ensure we disconnect on close
    End Sub
    Protected Overrides Sub WndProc(ByRef m As System.Windows.Forms.Message)

        netMessaging.ProcessMessage(m) ' Traiter les messages pour la communication inter-processus

        ' Appel crucial de la méthode de base pour le comportement standard
        MyBase.WndProc(m)
    End Sub
End Class
