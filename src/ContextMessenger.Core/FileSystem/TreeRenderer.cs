using System.IO;

namespace ContextMessenger.Core.FileSystem;

public static class TreeRenderer
{
    public static string Render(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var writer = new StringWriter() { NewLine = "\n" };
        RenderInto(writer, node, indent: 0);
        return writer.ToString();
    }

    private static void RenderInto(StringWriter writer, TreeNode node, int indent)
    {
        writer.Write(new string(' ', indent * 2));
        writer.Write(node.Name);
        if (node.IsDirectory) writer.Write('/');
        writer.WriteLine();

        foreach (var child in node.Children)
            RenderInto(writer, child, indent + 1);
    }
}
