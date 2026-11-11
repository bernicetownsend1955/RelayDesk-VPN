using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RelayDesk;

public sealed record ClipboardContent(string Text,string[] Files,BitmapSource? Image)
{
    public static async Task<ClipboardContent> Read()
    {
        for(int attempt=0;;attempt++)
        {
            try
            {
                var data=Clipboard.GetDataObject();if(data==null)return new("",[],null);
                string text=data.GetDataPresent(DataFormats.UnicodeText)?data.GetData(DataFormats.UnicodeText) as string??"":data.GetData(DataFormats.Text) as string??"";
                string[] files=data.GetDataPresent(DataFormats.FileDrop)?data.GetData(DataFormats.FileDrop) as string[]??[]:[];
                BitmapSource? image=Clipboard.ContainsImage()?Clipboard.GetImage():null;
                return new(text,files,image==null?null:QrImages.PrepareForDecode(image));
            }
            catch(ExternalException ex)when(ex.ErrorCode==unchecked((int)0x800401D0)&&attempt<9){await Task.Delay(75);}
        }
    }
}
