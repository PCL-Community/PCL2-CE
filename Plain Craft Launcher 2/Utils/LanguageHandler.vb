Imports Microsoft.Win32
Imports System.Globalization
Imports WPFLocalizeExtension.Extensions

Public Class LanguageHandler
    ' 改变语言
    Public Sub ChangeLanguage(ByVal languageCode As String)
        WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture = New CultureInfo(languageCode)
    End Sub

    ' 获取本地化字符串
    Public Shared Function GetLocalizedString(ByVal key As String, Optional ByVal resourceFileName As String = "Languages", Optional ByVal addSpaceAfter As Boolean = False) As String
        Dim localizedString As String = String.Empty

        ' 构建完整的键名
        Dim assemblyName As String = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name
        Dim fullKey As String = assemblyName & ":" & resourceFileName & ":" & key
        Dim locExtension As New LocExtension(fullKey)
        locExtension.ResolveLocalizedValue(localizedString)

        ' 若需要添加空格，则添加
        If addSpaceAfter Then
            localizedString &= " "
        End If

        Return localizedString
    End Function
End Class