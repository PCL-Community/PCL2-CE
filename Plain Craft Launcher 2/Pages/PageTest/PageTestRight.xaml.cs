using System.Windows.Input;

namespace PCL;

public partial class PageTestRight
{
    public PageTestRight()
    {
        InitializeComponent();
    }

    // 示例测试功能：弹出一条 Toast
    private void BtnToastTest_Click(object sender, MouseButtonEventArgs e)
    {
        HintService.Hint("测试 Toast：新弹窗系统工作正常", HintType.Info);
    }
}
