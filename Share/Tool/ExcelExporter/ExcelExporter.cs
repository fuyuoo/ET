using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using OfficeOpenXml;
using LicenseContext = OfficeOpenXml.LicenseContext;

/*
 Excel 导出工具说明（简要）
 - 用途：从 Excel 表生成配置类（c/s/cs），导出对应 JSON，再合并为二进制（BSON）bytes。
 - Excel 命名：文件名可带 @cs/@c/@s 指定导出目标，缺省等同于 cs；同一 proto 可拆分多个文件，以下划线后缀区分，protoName 取最后一个下划线之前部分。
 - 表头规范：
   第2行：字段 CS 标记（c/s/cs 或含 # 表示忽略该列）
   第3行：字段注释
   第4行：字段名
   第5行：字段类型
   第6行起：数据行；第2列为行 CS 前缀（c/s/cs，空视为 cs，含 # 跳过），第3列为 Id。
 - 输出：
   类文件：Client/Server/ClientServer 三处；
   JSON：Config/Json/{c|s|cs}/{相对路径}/xxx.txt；
   bytes：Config/Excel/{c|s|cs}/{相对路径}/{ProtoName}Category.bytes；并将 c 目录复制到 Unity/Assets/Bundles/Config。
*/

namespace ET
{
    /// <summary>
    /// 配置导出目标：c=客户端，s=服务端，cs=客户端与服务端
    /// </summary>
    public enum ConfigType
    {
        c = 0,
        s = 1,
        cs = 2,
    }

    /// <summary>
    /// 表头信息（按列汇总）：包含 CS 标记、注释、字段名、类型及顺序索引
    /// </summary>
    class HeadInfo
    {
        [BsonElement]
        public string FieldCS;
        public string FieldDesc;
        public string FieldName;
        public string FieldType;
        public int FieldIndex;

        public HeadInfo(string cs, string desc, string name, string type, int index)
        {
            this.FieldCS = cs;
            this.FieldDesc = desc;
            this.FieldName = name;
            this.FieldType = type;
            this.FieldIndex = index;
        }
    }

    // 这里加个标签是为了防止编译时裁剪掉protobuf，因为整个tool工程没有用到protobuf，编译会去掉引用，然后动态编译就会出错
    /// <summary>
    /// 同一 proto 聚合的表信息（跨工作表/跨文件合并字段定义）
    /// </summary>
    class Table
    {
        public bool C;   // 是否包含客户端导出
        public bool S;   // 是否包含服务端导出
        public int Index;
        public Dictionary<string, HeadInfo> HeadInfos = new Dictionary<string, HeadInfo>();
    }
    
    /// <summary>
    /// Excel 导出器：生成类文件 -> 动态编译 -> 导出 JSON -> 合并生成 BSON bytes
    /// </summary>
    public static class ExcelExporter
    {
        private static string template;

        // 生成类（客户端）输出目录
        private const string ClientClassDir = "../Unity/Assets/Scripts/Model/Generate/Client/Config";
        // 服务端因为机器人的存在必须包含客户端所有配置，所以单独的c字段没有意义,单独的c就表示cs
        // 生成类（服务端）输出目录
        private const string ServerClassDir = "../Unity/Assets/Scripts/Model/Generate/Server/Config";

        // 生成类（客户端+服务端）输出目录
        private const string CSClassDir = "../Unity/Assets/Scripts/Model/Generate/ClientServer/Config";

        // Excel 源目录
        private const string excelDir = "../Unity/Assets/Config/Excel/";

        // JSON 输出目录格式：Config/Json/{c|s|cs}/{相对路径}
        private const string jsonDir = "../Config/Json/{0}/{1}";

        // bytes（客户端）最终复制到 Bundles/Config
        private const string clientProtoDir = "../Unity/Assets/Bundles/Config";
        // bytes（服务端/通用）原始输出目录格式：Config/Excel/{c|s|cs}/{相对路径}
        private const string serverProtoDir = "../Config/Excel/{0}/{1}";
        private const string replaceStr = "/{0}/{1}";
        private static Assembly[] configAssemblies = new Assembly[3];

        private static Dictionary<string, Table> tables = new Dictionary<string, Table>();
        private static Dictionary<string, ExcelPackage> packages = new Dictionary<string, ExcelPackage>();

        /// <summary>
        /// 获取（或创建）同名 proto 的聚合表信息
        /// </summary>
        private static Table GetTable(string protoName)
        {
            if (!tables.TryGetValue(protoName, out var table))
            {
                table = new Table();
                tables[protoName] = table;
            }

            return table;
        }

        /// <summary>
        /// 打开并缓存 ExcelPackage（支持并行读）
        /// </summary>
        public static ExcelPackage GetPackage(string filePath)
        {
            if (!packages.TryGetValue(filePath, out var package))
            {
                using Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                package = new ExcelPackage(stream);
                packages[filePath] = package;
            }

            return package;
        }

        /// <summary>
        /// 导出主流程：清理输出 -> 扫描 Excel 生成类 -> 动态编译 -> 导出 JSON/bytes -> 复制客户端资源
        /// Excel 文件名规则：ProtoName[@cs|@c|@s][_{分片}].xlsx；不写 @ 时等同 cs
        /// </summary>
        public static void Export()
        {
            try
            {
                template = File.ReadAllText("Template.txt");
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                // 清理类输出目录
                if (Directory.Exists(ClientClassDir))
                {
                    Directory.Delete(ClientClassDir, true);
                }

                if (Directory.Exists(ServerClassDir))
                {
                    Directory.Delete(ServerClassDir, true);
                }

                if (Directory.Exists(CSClassDir))
                {
                    Directory.Delete(CSClassDir, true);
                }

                // 清理 JSON 与 bytes 输出目录
                string jsonProtoDirParent = jsonDir.Replace(replaceStr, string.Empty);
                if (Directory.Exists(jsonProtoDirParent))
                {
                    Directory.Delete(jsonProtoDirParent, true);
                }

                string serverProtoDirParent = serverProtoDir.Replace(replaceStr, string.Empty);
                if (Directory.Exists(serverProtoDirParent))
                {
                    Directory.Delete(serverProtoDirParent, true);
                }

                // 收集类定义（跨文件/工作表合并）
                List<string> files = FileHelper.GetAllFiles(excelDir);
                foreach (string path in files)
                {
                    string fileName = Path.GetFileName(path);
                    if (!fileName.EndsWith(".xlsx") || fileName.StartsWith("~$") || fileName.Contains("#"))
                    {
                        continue; // 临时文件或标记文件跳过
                    }

                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                    string fileNameWithoutCS = fileNameWithoutExtension;
                    string cs = "cs"; // 默认导出到 cs
                    if (fileNameWithoutExtension.Contains("@"))
                    {
                        string[] ss = fileNameWithoutExtension.Split("@");
                        fileNameWithoutCS = ss[0];
                        cs = ss[1];
                    }

                    if (cs == "")
                    {
                        cs = "cs";
                    }

                    ExcelPackage p = GetPackage(Path.GetFullPath(path));

                    // 支持同一 proto 拆分：取最后一个下划线之前部分作为 protoName
                    string protoName = fileNameWithoutCS;
                    if (fileNameWithoutCS.Contains('_'))
                    {
                        protoName = fileNameWithoutCS.Substring(0, fileNameWithoutCS.LastIndexOf('_'));
                    }

                    Table table = GetTable(protoName);

                    if (cs.Contains("c"))
                    {
                        table.C = true;
                    }

                    if (cs.Contains("s"))
                    {
                        table.S = true;
                    }

                    ExportExcelClass(p, protoName, table);
                }

                // 按 c/s/cs 维度导出类文件
                foreach (var kv in tables)
                {
                    if (kv.Value.C)
                    {
                        ExportClass(kv.Key, kv.Value.HeadInfos, ConfigType.c);
                    }
                    if (kv.Value.S)
                    {
                        ExportClass(kv.Key, kv.Value.HeadInfos, ConfigType.s);
                    }
                    ExportClass(kv.Key, kv.Value.HeadInfos, ConfigType.cs);
                }

                // 动态编译生成的配置代码
                configAssemblies[(int) ConfigType.c] = DynamicBuild(ConfigType.c);
                configAssemblies[(int) ConfigType.s] = DynamicBuild(ConfigType.s);
                configAssemblies[(int) ConfigType.cs] = DynamicBuild(ConfigType.cs);

                // 导出 JSON 与 bytes
                List<string> excels = FileHelper.GetAllFiles(excelDir, "*.xlsx");
                
                foreach (string path in excels)
                {
                    ExportExcel(path);
                }
                
                // 将客户端 bytes 统一复制到 Bundles/Config
                if (Directory.Exists(clientProtoDir))
                {
                    Directory.Delete(clientProtoDir, true);
                }
                FileHelper.CopyDirectory("../Config/Excel/c", clientProtoDir);
                
                Log.Console("Export Excel Sucess!");
            }
            catch (Exception e)
            {
                Log.Console(e.ToString());
            }
            finally
            {
                tables.Clear();
                foreach (var kv in packages)
                {
                    kv.Value.Dispose();
                }

                packages.Clear();
            }
        }

        /// <summary>
        /// 导出单个 Excel：按 c/s/cs 过滤导出 JSON 与 bytes
        /// </summary>
        private static void ExportExcel(string path)
        {
            string dir = Path.GetDirectoryName(path);
            string relativePath = Path.GetRelativePath(excelDir, dir);
            string fileName = Path.GetFileName(path);
            if (!fileName.EndsWith(".xlsx") || fileName.StartsWith("~$") || fileName.Contains("#"))
            {
                return;
            }

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string fileNameWithoutCS = fileNameWithoutExtension;
            string cs = "cs";
            if (fileNameWithoutExtension.Contains("@"))
            {
                string[] ss = fileNameWithoutExtension.Split("@");
                fileNameWithoutCS = ss[0];
                cs = ss[1];
            }
            
            if (cs == "")
            {
                cs = "cs";
            }

            string protoName = fileNameWithoutCS;
            if (fileNameWithoutCS.Contains('_'))
            {
                protoName = fileNameWithoutCS.Substring(0, fileNameWithoutCS.LastIndexOf('_'));
            }

            Table table = GetTable(protoName);

            ExcelPackage p = GetPackage(Path.GetFullPath(path));

            if (cs.Contains("c"))
            {
                ExportExcelJson(p, fileNameWithoutCS, table, ConfigType.c, relativePath);
                ExportExcelProtobuf(ConfigType.c, protoName, relativePath);
            }

            if (cs.Contains("s"))
            {
                ExportExcelJson(p, fileNameWithoutCS, table, ConfigType.s, relativePath);
                ExportExcelProtobuf(ConfigType.s, protoName, relativePath);
            }
            ExportExcelJson(p, fileNameWithoutCS, table, ConfigType.cs, relativePath);
            ExportExcelProtobuf(ConfigType.cs, protoName, relativePath);
        }

        private static string GetProtoDir(ConfigType configType, string relativeDir)
        {
            return string.Format(serverProtoDir, configType.ToString(), relativeDir);
        }

        private static Assembly GetAssembly(ConfigType configType)
        {
            return configAssemblies[(int) configType];
        }

        private static string GetClassDir(ConfigType configType)
        {
            return configType switch
            {
                ConfigType.c => ClientClassDir,
                ConfigType.s => ServerClassDir,
                _ => CSClassDir
            };
        }
        
        // 动态编译生成的cs代码
        /// <summary>
        /// 动态编译指定目标（c/s/cs）的已生成类，供 JSON 反序列化与合并使用
        /// </summary>
        private static Assembly DynamicBuild(ConfigType configType)
        {
            string classPath = GetClassDir(configType);
            List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();
            List<string> protoNames = new List<string>();
            foreach (string classFile in Directory.GetFiles(classPath, "*.cs"))
            {
                protoNames.Add(Path.GetFileNameWithoutExtension(classFile));
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(classFile)));
            }

            List<PortableExecutableReference> references = new List<PortableExecutableReference>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                try
                {
                    if (assembly.IsDynamic)
                    {
                        continue;
                    }

                    if (assembly.Location == "")
                    {
                        continue;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }

                PortableExecutableReference reference = MetadataReference.CreateFromFile(assembly.Location);
                references.Add(reference);
            }

            CSharpCompilation compilation = CSharpCompilation.Create(null,
                syntaxTrees.ToArray(),
                references.ToArray(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using MemoryStream memSteam = new MemoryStream();

            EmitResult emitResult = compilation.Emit(memSteam);
            if (!emitResult.Success)
            {
                StringBuilder stringBuilder = new StringBuilder();
                foreach (Diagnostic t in emitResult.Diagnostics)
                {
                    stringBuilder.Append($"{t.GetMessage()}\n");
                }

                throw new Exception($"动态编译失败:\n{stringBuilder}");
            }

            memSteam.Seek(0, SeekOrigin.Begin);

            Assembly ass = Assembly.Load(memSteam.ToArray());
            return ass;
        }


        #region 导出class

        /// <summary>
        /// 遍历所有工作表并收集表头定义
        /// </summary>
        static void ExportExcelClass(ExcelPackage p, string name, Table table)
        {
            foreach (ExcelWorksheet worksheet in p.Workbook.Worksheets)
            {
                ExportSheetClass(worksheet, table);
            }
        }

        /// <summary>
        /// 解析工作表表头定义：
        /// 第2行=CS 标记；第3行=注释；第4行=字段名；第5行=类型；第3列起为有效字段列
        /// 列 CS 含 # 表示废弃该列（不导出）
        /// </summary>
        static void ExportSheetClass(ExcelWorksheet worksheet, Table table)
        {
            const int row = 2;
            for (int col = 3; col <= worksheet.Dimension.End.Column; ++col)
            {
                if (worksheet.Name.StartsWith("#"))
                {
                    continue;
                }

                string fieldName = worksheet.Cells[row + 2, col].Text.Trim();
                if (fieldName == "")
                {
                    continue;
                }

                if (table.HeadInfos.ContainsKey(fieldName))
                {
                    continue;
                }

                string fieldCS = worksheet.Cells[row, col].Text.Trim().ToLower();
                if (fieldCS.Contains("#"))
                {
                    table.HeadInfos[fieldName] = null; // 显式忽略该列
                    continue;
                }
                
                if (fieldCS == "")
                {
                    fieldCS = "cs"; // 缺省视为 cs
                }

                if (table.HeadInfos.TryGetValue(fieldName, out var oldClassField))
                {
                    if (oldClassField.FieldCS != fieldCS)
                    {
                        Log.Console($"field cs not same: {worksheet.Name} {fieldName} oldcs: {oldClassField.FieldCS} {fieldCS}");
                    }

                    continue;
                }

                string fieldDesc = worksheet.Cells[row + 1, col].Text.Trim();
                string fieldType = worksheet.Cells[row + 3, col].Text.Trim();

                table.HeadInfos[fieldName] = new HeadInfo(fieldCS, fieldDesc, fieldName, fieldType, ++table.Index);
            }
        }

        /// <summary>
        /// 根据收集的表头生成类文件，按导出目标（c/s/cs）过滤字段
        /// </summary>
        static void ExportClass(string protoName, Dictionary<string, HeadInfo> classField, ConfigType configType)
        {
            string dir = GetClassDir(configType);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string exportPath = Path.Combine(dir, $"{protoName}.cs");

            using FileStream txt = new FileStream(exportPath, FileMode.Create);
            using StreamWriter sw = new StreamWriter(txt);

            StringBuilder sb = new StringBuilder();
            foreach ((string _, HeadInfo headInfo) in classField)
            {
                if (headInfo == null)
                {
                    continue;
                }

                if (configType != ConfigType.cs && !headInfo.FieldCS.Contains(configType.ToString()))
                {
                    continue;
                }

                sb.Append($"\t\t/// <summary>{headInfo.FieldDesc}</summary>\n");
                string fieldType = headInfo.FieldType;
                sb.Append($"\t\tpublic {fieldType} {headInfo.FieldName} {{ get; set; }}\n");
            }

            string content = template.Replace("(ConfigName)", protoName).Replace(("(Fields)"), sb.ToString());
            sw.Write(content);
        }

        #endregion

        #region 导出json


        /// <summary>
        /// 导出整个 Excel 的 JSON（dict 数组形式），每个工作表依次追加
        /// </summary>
        static void ExportExcelJson(ExcelPackage p, string name, Table table, ConfigType configType, string relativeDir)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"dict\": [\n");
            foreach (ExcelWorksheet worksheet in p.Workbook.Worksheets)
            {
                if (worksheet.Name.StartsWith("#"))
                {
                    continue;
                }

                ExportSheetJson(worksheet, name, table.HeadInfos, configType, sb);
            }

            sb.Append("]}\n");

            string dir = string.Format(jsonDir, configType.ToString(), relativeDir);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string jsonPath = Path.Combine(dir, $"{name}.txt");
            using FileStream txt = new FileStream(jsonPath, FileMode.Create);
            using StreamWriter sw = new StreamWriter(txt);
            sw.Write(sb.ToString());
        }

        /// <summary>
        /// 导出单个工作表的数据：
        /// - 数据从第6行开始；第2列为行 CS 前缀（含 # 跳过，空视为 cs）
        /// - 第3列为 Id（必须）；字段从第3列起；字段名取第4行
        /// - 字段名为 Id 时导出为 _id
        /// </summary>
        static void ExportSheetJson(ExcelWorksheet worksheet, string name, 
                Dictionary<string, HeadInfo> classField, ConfigType configType, StringBuilder sb)
        {
            string configTypeStr = configType.ToString();
            for (int row = 6; row <= worksheet.Dimension.End.Row; ++row)
            {
                string prefix = worksheet.Cells[row, 2].Text.Trim();
                if (prefix.Contains("#"))
                {
                    continue;
                }

                if (prefix == "")
                {
                    prefix = "cs";
                }
                
                if (configType != ConfigType.cs && !prefix.Contains(configTypeStr))
                {
                    continue;
                }

                if (worksheet.Cells[row, 3].Text.Trim() == "")
                {
                    continue; // 无 Id 的行不导出
                }

                sb.Append($"[{worksheet.Cells[row, 3].Text.Trim()}, {{\"_t\":\"{name}\"");
                for (int col = 3; col <= worksheet.Dimension.End.Column; ++col)
                {
                    string fieldName = worksheet.Cells[4, col].Text.Trim();
                    if (!classField.ContainsKey(fieldName))
                    {
                        continue;
                    }

                    HeadInfo headInfo = classField[fieldName];

                    if (headInfo == null)
                    {
                        continue;
                    }

                    if (configType != ConfigType.cs && !headInfo.FieldCS.Contains(configTypeStr))
                    {
                        continue;
                    }

                    string fieldN = headInfo.FieldName;
                    if (fieldN == "Id")
                    {
                        fieldN = "_id";
                    }

                    sb.Append($",\"{fieldN}\":{Convert(headInfo.FieldType, worksheet.Cells[row, col].Text.Trim())}");
                }

                sb.Append("}],\n");
            }
        }

        /// <summary>
        /// 类型转换与格式化：数组原样包裹，数字空值为0，字符串做转义并加引号
        /// </summary>
        private static string Convert(string type, string value)
        {
            switch (type)
            {
                case "uint[]":
                case "int[]":
                case "int32[]":
                case "long[]":
                    return $"[{value}]";
                case "string[]":
                case "int[][]":
                    return $"[{value}]";
                case "int":
                case "uint":
                case "int32":
                case "int64":
                case "long":
                case "float":
                case "double":
                    if (value == "")
                    {
                        return "0";
                    }

                    return value;
                case "string":
                    value = value.Replace("\\", "\\\\");
                    value = value.Replace("\"", "\\\"");
                    return $"\"{value}\"";
                default:
                    throw new Exception($"不支持此类型: {type}");
            }
        }

        #endregion


        // 根据生成的类，把json转成protobuf
        /// <summary>
        /// 合并同名 JSON（支持多分片，按文件名倒序）并序列化为 BSON bytes：{ProtoName}Category.bytes
        /// </summary>
        private static void ExportExcelProtobuf(ConfigType configType, string protoName, string relativeDir)
        {
            string dir = GetProtoDir(configType, relativeDir);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Assembly ass = GetAssembly(configType);
            Type type = ass.GetType($"ET.{protoName}Category");
            Type subType = ass.GetType($"ET.{protoName}");

            IMerge final = Activator.CreateInstance(type) as IMerge;

            string p = Path.Combine(string.Format(jsonDir, configType, relativeDir));
            string[] ss = Directory.GetFiles(p, $"{protoName}*.txt");
            List<string> jsonPaths = ss.ToList();

            jsonPaths.Sort();
            jsonPaths.Reverse();
            foreach (string jsonPath in jsonPaths)
            {
                string json = File.ReadAllText(jsonPath);
                try
                {
                    object deserialize = BsonSerializer.Deserialize(json, type);
                    final.Merge(deserialize);
                }
                catch (Exception e)
                {
                    throw new Exception($"json : {jsonPath} error", e);
                }
            }

            string path = Path.Combine(dir, $"{protoName}Category.bytes");

            using FileStream file = File.Create(path);
            file.Write(final.ToBson());
        }
    }
}
