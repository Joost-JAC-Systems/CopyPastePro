using System.IO;

namespace CopyPastePro.Services;

/// <summary>Central file-extension lists for classification, images, and sync folder scanning.</summary>
public static class FileExtensionCatalog
{
  public static readonly string[] ImageExtensions =
  [
    ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".bmp", ".dib", ".webp", ".avif", ".heic", ".heif",
    ".ico", ".cur", ".ani", ".svg", ".svgz", ".tif", ".tiff", ".jp2", ".j2k", ".jpf", ".jpx", ".jpm",
    ".psd", ".psb", ".xcf", ".exr", ".hdr", ".dds", ".tga", ".pcx", ".pnm", ".ppm", ".pgm", ".pbm",
    ".raw", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".orf", ".rw2", ".dng", ".raf", ".pef", ".srw",
    ".emf", ".wmf", ".jxl", ".bpg", ".qoi"
  ];

  public static readonly IReadOnlyDictionary<string, string[]> CategoryExtensions =
      new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
      {
        ["Image"] = ImageExtensions,
        ["Code"] =
        [
          ".cs", ".csx", ".vb", ".fs", ".fsx", ".fsi", ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp", ".hh",
          ".hxx", ".inl", ".m", ".mm", ".swift", ".kt", ".kts", ".java", ".class", ".jar", ".scala",
          ".sc", ".groovy", ".gradle", ".clj", ".cljs", ".edn", ".erl", ".hrl", ".ex", ".exs", ".eex",
          ".heex", ".leex", ".jl", ".lua", ".luau", ".pl", ".pm", ".t", ".r", ".R", ".m", ".mat",
          ".py", ".pyw", ".pyi", ".pyc", ".pyo", ".ipynb", ".rb", ".erb", ".rake", ".php", ".phtml",
          ".js", ".mjs", ".cjs", ".jsx", ".ts", ".mts", ".cts", ".tsx", ".vue", ".svelte", ".astro",
          ".go", ".mod", ".sum", ".rs", ".rlib", ".toml", ".zig", ".nim", ".v", ".sv", ".svh", ".vhd",
          ".vhdl", ".asm", ".s", ".S", ".dart", ".pas", ".pp", ".dpr", ".f", ".f90", ".f95", ".for",
          ".cob", ".cbl", ".cpy", ".sql", ".psql", ".mysql", ".hql", ".graphql", ".gql",
          ".html", ".htm", ".xhtml", ".shtml", ".css", ".scss", ".sass", ".less", ".styl",
          ".xml", ".xsl", ".xslt", ".xsd", ".dtd", ".rss", ".atom", ".svg", ".vue",
          ".json", ".jsonc", ".json5", ".jsonl", ".ndjson", ".yaml", ".yml", ".yml.dist",
          ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1", ".psd1", ".ps1xml", ".bat", ".cmd",
          ".awk", ".sed", ".vim", ".vimrc", ".nvim", ".el", ".lisp", ".cl", ".scm", ".rkt",
          ".dockerfile", ".containerfile", ".make", ".mk", ".cmake", ".ninja", ".bazel", ".bzl",
          ".proto", ".thrift", ".avsc", ".wasm", ".wat", ".wast", ".hlsl", ".glsl", ".vert", ".frag",
          ".in", ".patch", ".diff"
        ],
        ["Document"] =
        [
          ".txt", ".text", ".md", ".markdown", ".mdown", ".mkd", ".rst", ".adoc", ".asciidoc",
          ".rtf", ".doc", ".docx", ".docm", ".dot", ".dotx", ".odt", ".ott", ".fodt",
          ".pdf", ".xps", ".oxps", ".epub", ".mobi", ".azw", ".azw3", ".fb2", ".djvu", ".djv",
          ".tex", ".latex", ".ltx", ".bib", ".wpd", ".wps", ".pages", ".abw", ".zabw", ".log", ".readme"
        ],
        ["Spreadsheet"] =
        [
          ".xls", ".xlsx", ".xlsm", ".xlsb", ".xlt", ".xltx", ".xltm", ".ods", ".ots", ".fods",
          ".csv", ".tsv", ".tab", ".numbers", ".sylk", ".slk"
        ],
        ["Presentation"] =
        [
          ".ppt", ".pptx", ".pptm", ".pot", ".potx", ".potm", ".pps", ".ppsx", ".ppsm",
          ".odp", ".otp", ".fodp", ".key", ".sxi"
        ],
        ["Archive"] =
        [
          ".zip", ".zipx", ".rar", ".7z", ".7zip", ".tar", ".gz", ".gzip", ".tgz", ".bz2", ".tbz2",
          ".xz", ".txz", ".zst", ".zstd", ".lz", ".lzma", ".lzo", ".lz4", ".cab", ".arj", ".ace",
          ".iso", ".img", ".dmg", ".toast", ".vhd", ".vhdx", ".vmdk", ".ova", ".ovf", ".jar", ".war",
          ".ear", ".apk", ".aar", ".deb", ".rpm", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle",
          ".snap", ".pkg", ".xpi", ".crx"
        ],
        ["Audio"] =
        [
          ".mp3", ".wav", ".wave", ".flac", ".aac", ".m4a", ".m4b", ".m4p", ".m4r", ".ogg", ".oga",
          ".opus", ".wma", ".aiff", ".aif", ".aifc", ".caf", ".mid", ".midi", ".kar", ".mod", ".s3m",
          ".xm", ".it", ".wv", ".ape", ".alac", ".dts", ".ac3", ".amr", ".3gp", ".gsm"
        ],
        ["Video"] =
        [
          ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".qt", ".wmv", ".asf", ".webm", ".ogv", ".ogg",
          ".mpeg", ".mpg", ".mpe", ".m2v", ".mpv", ".mp2", ".vob", ".flv", ".f4v", ".swf", ".3gp",
          ".3g2", ".mts", ".m2ts", ".ts", ".mxf", ".rm", ".rmvb", ".divx", ".amv", ".wtv", ".dvr-ms"
        ],
        ["Executable"] =
        [
          ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".com", ".scr", ".pif",
          ".dll", ".ocx", ".sys", ".drv", ".cpl", ".msp", ".mst", ".app", ".dmg", ".pkg", ".run",
          ".bin", ".elf", ".so", ".dylib", ".deb", ".rpm", ".apk", ".ipa"
        ],
        ["Design"] =
        [
          ".psd", ".psb", ".ai", ".eps", ".epsf", ".pdf", ".fig", ".sketch", ".xd", ".xdd",
          ".indd", ".indt", ".idml", ".afdesign", ".afphoto", ".afpub", ".clip", ".csp",
          ".blend", ".blend1", ".max", ".3ds", ".fbx", ".obj", ".dae", ".gltf", ".glb", ".stl",
          ".step", ".stp", ".iges", ".igs", ".dwg", ".dxf", ".skp", ".cdr", ".cmx"
        ],
        ["Data"] =
        [
          ".db", ".sqlite", ".sqlite3", ".db3", ".sdb", ".mdb", ".accdb", ".accde", ".accdr",
          ".mdf", ".ndf", ".ldf", ".frm", ".ibd", ".parquet", ".avro", ".orc", ".feather",
          ".arrow", ".hdf5", ".h5", ".hdf", ".nc", ".cdf", ".fits", ".fits", ".mat", ".sav",
          ".dta", ".sas7bdat", ".xpt", ".json", ".jsonl", ".ndjson", ".xml", ".csv", ".tsv"
        ],
        ["Font"] = [".ttf", ".otf", ".woff", ".woff2", ".eot", ".ttc", ".fon", ".fnt"],
        ["Certificate"] = [".pem", ".crt", ".cer", ".der", ".p12", ".pfx", ".p7b", ".p7c", ".key", ".csr"],
        ["Config"] =
        [
          ".ini", ".cfg", ".conf", ".config", ".properties", ".env", ".env.local", ".env.example",
          ".reg", ".inf", ".plist", ".prefs", ".editorconfig", ".gitignore", ".gitattributes",
          ".dockerignore", ".npmrc", ".nvmrc", ".prettierrc", ".eslintrc"
        ],
        ["Email"] = [".eml", ".msg", ".mbox", ".pst", ".ost", ".vcf", ".ics", ".vcs"],
        ["Subtitle"] = [".srt", ".vtt", ".ass", ".ssa", ".sub", ".idx", ".sbv", ".ttml"],
        ["Shortcut"] = [".lnk", ".url", ".webloc", ".desktop", ".website"],
        ["Ebook"] = [".epub", ".mobi", ".azw", ".azw3", ".fb2", ".lit", ".prc", ".cbz", ".cbr"],
        ["3D"] = [".obj", ".fbx", ".gltf", ".glb", ".stl", ".ply", ".dae", ".3mf", ".blend", ".usdz"],
        ["CAD"] = [".dwg", ".dxf", ".step", ".stp", ".iges", ".igs", ".skp", ".ifc", ".rvt", ".fcstd"]
      };

  private static readonly HashSet<string> ImageExtensionSet =
      new(ImageExtensions, StringComparer.OrdinalIgnoreCase);

  public static bool IsImageExtension(string? extension)
  {
    if (string.IsNullOrWhiteSpace(extension)) return false;
    var ext = extension.StartsWith('.') ? extension : "." + extension;
    return ImageExtensionSet.Contains(ext);
  }

  public static bool IsImageFilePath(string? path) =>
      !string.IsNullOrEmpty(path) && IsImageExtension(Path.GetExtension(path));

  public static string? GetBuiltInCategory(string extension)
  {
    if (string.IsNullOrWhiteSpace(extension)) return null;
    var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    foreach (var (category, extensions) in CategoryExtensions)
    {
      if (category == "Image") continue; // Image handled by content type first
      if (extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        return category;
    }
    return null;
  }

  public static IReadOnlyList<string> AllBuiltInCategories()
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "All", "General", "Text", "LongText", "Image", "Files", "Link", "Web"
    };
    foreach (var key in CategoryExtensions.Keys)
      set.Add(key);
    return set.OrderBy(c => c == "All" ? "" : c).ToList();
  }
}
