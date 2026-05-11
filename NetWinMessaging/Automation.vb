
Imports System.IO.MemoryMappedFiles
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

Public Class MessageReceivedEventArgs
    Inherits EventArgs

    Public ReadOnly Property SenderHwnd As IntPtr
    Public ReadOnly Property Message As String

    Public Sub New(senderHwnd As IntPtr, message As String)
        Me.SenderHwnd = senderHwnd
        Me.Message = message
    End Sub
End Class


Public Class Automation

    ' Per-identifier shared memory: name = Prefix + "_" + identifier
    ' Layout (lock-free sequence):
    ' 0..7   : Int64 version
    ' 8..11  : Int32 window handle
    Private Shared ReadOnly shmPrefix As String = "NetWinMessaging_Instance"
    Private Const VERSION_SIZE As Integer = 8
    Private Const HANDLE_SIZE As Integer = 4
    Private Const SHM_VERSION As Long = 20260618L
    Private Const SHM_SIZE As Integer = VERSION_SIZE + HANDLE_SIZE

    Private Const WM_COPYDATA As UInteger = &H4A
    Private Const WM_USER As UInteger = &H400
    Public Const WM_HELLO As UInteger = WM_USER + &H1

    <StructLayout(LayoutKind.Sequential)>
    Private Structure COPYDATASTRUCT
        Public dwData As IntPtr
        Public cbData As Integer
        Public lpData As IntPtr
    End Structure


    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function IsWindow(hWnd As IntPtr) As Boolean
    End Function


    <DllImport("user32.dll")>
    Private Shared Function SendMessage(ByVal hwnd As IntPtr,
                                          ByVal wMsg As Integer,
                                          ByVal wParam As Integer,
                                          ByVal lParam As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessage(
                                          ByVal hwnd As IntPtr,
                                          ByVal wMsg As Integer,
                                          ByVal wParam As Integer,
                                          <MarshalAs(UnmanagedType.LPTStr)> ByVal lParam As String) As Integer
    End Function

    Private connected As Boolean
    Private messagingId As String
    Private shmMessaging As MemoryMappedFile = Nothing

    Public Event MessageReceived As EventHandler(Of MessageReceivedEventArgs)

    Public Sub New()
        Initialize()
    End Sub

    Private Sub Initialize()
        connected = False
        messagingId = String.Empty
    End Sub

    Private Function GetWindowHandle() As IntPtr

        Dim mainHandle As IntPtr = Process.GetCurrentProcess().MainWindowHandle
        If mainHandle <> IntPtr.Zero Then
            Return mainHandle.ToInt32()
        End If

        Return IntPtr.Zero
    End Function

    Public Property Identifier As String
        Get
            Return messagingId
        End Get
        Set(value As String)
            messagingId = value
        End Set
    End Property

    Public ReadOnly Property IsConnected As Boolean
        Get
            Return connected
        End Get
    End Property

    Protected Overrides Sub Finalize()
        Disconnect()
        MyBase.Finalize()
    End Sub

    Public Function Connect(identifier As String) As Boolean

        Dim ret As Boolean = False

        If IsConnected Then
            Return ret
        End If

        If (String.IsNullOrEmpty(identifier)) Then
            Return ret
        End If

        Try
            Dim windowHandlePtr = GetWindowHandle()
            If (windowHandlePtr = IntPtr.Zero) OrElse Not IsWindow(windowHandlePtr) Then
                Return ret
            End If

            If Not (PublishThisInstance(windowHandlePtr)) Then
                Return ret
            End If

            connected = True
            ret = True

        Catch ex As Exception
            Throw New InvalidOperationException("Failed to connect and publish handle", ex)
        End Try

        Return ret

    End Function

    Public Function Disconnect() As Boolean

        If Not IsConnected Then
            Return False
        End If

        If (shmMessaging IsNot Nothing) Then
            shmMessaging.Dispose()
            shmMessaging = Nothing
        End If

        connected = False

        Return True
    End Function

    Private Function ReadHandleByIdentifier(identifier As String, ByRef windowHandle As IntPtr) As Boolean

        Dim ret As Boolean = False

        windowHandle = IntPtr.Zero

        If String.IsNullOrEmpty(identifier) Then Return ret

        Dim mmfName = $"{shmPrefix}_{identifier}"

        Try

            ret = True

            Using mmf = MemoryMappedFile.OpenExisting(mmfName)
                Using acc = mmf.CreateViewAccessor(0, SHM_SIZE, MemoryMappedFileAccess.Read)
                    Dim version As Long = acc.ReadInt64(0)
                    Dim handle As Integer = acc.ReadInt32(VERSION_SIZE)
                    If version = SHM_VERSION Then
                        windowHandle = New IntPtr(handle)
                        Return ret
                    End If

                End Using
            End Using

        Catch
            ' treat as not present
            ret = False
        End Try

        Return ret
    End Function

    Private Function PublishThisInstance(windowHandlePtr As IntPtr) As Boolean
        Dim ret As Boolean = False

        If String.IsNullOrEmpty(messagingId) OrElse windowHandlePtr = IntPtr.Zero Then Return ret

        Try
            PublishHandleByIdentifier(messagingId, windowHandlePtr.ToInt32())
            ret = True
        Catch ex As Exception
            Throw New InvalidOperationException("Failed to publish this instance", ex)
        End Try

        Return ret

    End Function

    Public Function SendToIdentifier(targetIdentifier As String, message As String) As Boolean

        Dim ret As Boolean = False

        If String.IsNullOrEmpty(targetIdentifier) OrElse message Is Nothing Then
            Return ret
        End If

        Dim targetHandle As IntPtr = IntPtr.Zero
        If Not (ReadHandleByIdentifier(targetIdentifier, targetHandle)) Then
            Return ret
        End If

        Dim windowHandlePtr = GetWindowHandle()
        If windowHandlePtr = IntPtr.Zero Then
            Return ret
        End If


        Dim result As IntPtr = IntPtr.Zero
        Dim dataPtr As IntPtr = IntPtr.Zero
        Dim cdsPtr As IntPtr = IntPtr.Zero

        Try
            ' Use Unicode (wide chars). Ensure a terminating NUL so the receiver can treat it as a C string if needed.
            Dim payload As Byte() = Encoding.Unicode.GetBytes(message & ChrW(0))
            dataPtr = Marshal.AllocHGlobal(payload.Length)
            Marshal.Copy(payload, 0, dataPtr, payload.Length)

            Dim cds As COPYDATASTRUCT
            cds.dwData = IntPtr.Zero ' optional user data (use if you want to pass a message type)
            cds.cbData = payload.Length
            cds.lpData = dataPtr

            cdsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(GetType(COPYDATASTRUCT)))
            Marshal.StructureToPtr(cds, cdsPtr, False)

            ' SendMessage is required for WM_COPYDATA so the OS marshals the data to the target process.
            result = SendMessage(targetHandle, WM_COPYDATA, windowHandlePtr.ToInt32(), cdsPtr.ToInt32())
            ret = result <> IntPtr.Zero
        Finally
            If cdsPtr <> IntPtr.Zero Then
                Marshal.DestroyStructure(cdsPtr, GetType(COPYDATASTRUCT))
                Marshal.FreeHGlobal(cdsPtr)
            End If
            If dataPtr <> IntPtr.Zero Then
                Marshal.FreeHGlobal(dataPtr)
            End If
        End Try

        Return ret
    End Function

    ' Publish this instance handle under a per-identifier.
    Private Function PublishHandleByIdentifier(identifier As String, hwnd As Integer) As Boolean

        Dim ret As Boolean = False

        Try
            If String.IsNullOrEmpty(identifier) Then Throw New ArgumentException("identifier")

            If (shmMessaging IsNot Nothing) Then
                shmMessaging.Dispose()
                shmMessaging = Nothing
            End If

            Dim mmfName = $"{shmPrefix}_{identifier}"
            shmMessaging = MemoryMappedFile.CreateOrOpen(mmfName, SHM_SIZE, MemoryMappedFileAccess.ReadWrite)
            Using acc = shmMessaging.CreateViewAccessor(0, SHM_SIZE, MemoryMappedFileAccess.ReadWrite)
                acc.Write(0, SHM_VERSION)
                acc.Write(VERSION_SIZE, hwnd)
            End Using

            ret = True

        Catch ex As Exception
            Throw New InvalidOperationException("Failed to publish handle by identifier", ex)
        End Try

        Return ret
    End Function

    Public Function ProcessMessage(ByRef m As Message) As Boolean
        Dim ret As Boolean = False

        Select Case m.Msg
            Case WM_COPYDATA
                Try
                    ' Marshal the COPYDATASTRUCT pointed to by lParam
                    Dim cds As COPYDATASTRUCT = Marshal.PtrToStructure(Of COPYDATASTRUCT)(m.LParam)

                    If cds.cbData <= 0 OrElse cds.lpData = IntPtr.Zero Then
                        Return ret
                    End If

                    ' Copy payload bytes and decode as Unicode (matches SendToIdentifier)
                    Dim buffer(cds.cbData - 1) As Byte
                    Marshal.Copy(cds.lpData, buffer, 0, cds.cbData)

                    Dim text As String = Encoding.Unicode.GetString(buffer)
                    text = text.TrimEnd(ChrW(0))


                    RaiseEvent MessageReceived(Me, New MessageReceivedEventArgs(m.WParam, text))

                    ret = True
                Catch

                End Try

        End Select

        Return ret
    End Function
End Class
