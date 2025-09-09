### Excel 导出规则（基于 `Share/Tool/ExcelExporter/ExcelExporter.cs`）

本规则定义了如何从 Excel 配置生成 C# 类、JSON 文本与二进制 bytes（BSON），用于客户端与服务端加载配置。

- **工具入口**: `ET.ExcelExporter.Export()`
- **Excel 源目录**: `Unity/Assets/Config/Excel/`
- **类输出目录**:
  - `c`（客户端）: `Unity/Assets/Scripts/Model/Generate/Client/Config`
  - `s`（服务端）: `Unity/Assets/Scripts/Model/Generate/Server/Config`
  - `cs`（客户端+服务端）: `Unity/Assets/Scripts/Model/Generate/ClientServer/Config`
- **JSON 输出目录**: `Config/Json/{c|s|cs}/{相对目录}/xxx.txt`
- **bytes 输出目录**: `Config/Excel/{c|s|cs}/{相对目录}/{ProtoName}Category.bytes`
- **客户端资源复制**: 将 `Config/Excel/c` 复制到 `Unity/Assets/Bundles/Config`


### 1. Excel 文件命名

- 格式：`ProtoName[@cs|@c|@s][_{分片}].xlsx`
  - `@cs`/`@c`/`@s` 表示导出目标；省略时等同于 `@cs`
  - 当同一 `ProtoName` 需要拆分到多个文件时，在文件名后追加下划线分片后缀，例如：`Item_1.xlsx`、`Item_2.xlsx`
  - `ProtoName` 的确定：取去掉 `@xxx` 和最后一个下划线 `_` 及其之后部分的前缀
- 过滤：临时文件或被标记的文件会被跳过
  - 跳过条件：文件名以 `~$` 开头或文件名包含 `#`


### 2. 工作表与表头规范

同一个 Excel 文件可包含多个工作表，导出时逐表处理。若工作表名以 `#` 开头，则跳过整个工作表。

- 列从第3列开始（第1、2列是控制/索引列），行含义如下：
  - 第2行：字段 CS 标记，支持 `c`/`s`/`cs`；若包含 `#` 则忽略该列；为空等同 `cs`
  - 第3行：字段注释（生成类时作为 XML 文档注释）
  - 第4行：字段名（例如 `Id`，特殊：导出 JSON 时字段名为 `Id` 会被映射为 `_id`）
  - 第5行：字段类型（见“类型映射”章节）
  - 第6行起：数据行

- 列忽略规则：
  - 若第2行（CS 标记）包含 `#`，此列不会导出（工具内部以空占位保存，表示显式忽略）


### 3. 数据行规则

- 行控制列：
  - 第2列为行级 CS 前缀，支持 `c`/`s`/`cs`，为空等同 `cs`；若包含 `#` 则整行跳过
  - 第3列为主键 `Id`（必填，用于 JSON 输出时的第一元素值以及 `_id` 字段）
- 字段起始列为第3列；字段名取第4行；工具按列遍历并根据表头过滤
- JSON 结构：将每一行导出为 `[{Id}, {"_t":"{表名}", ...字段...}],`，最后包裹为 `{ "dict": [ ... ] }`
- 多工作表：同一 Excel 文件的各工作表导出的行会依次追加到同一个 `dict` 数组中


### 4. 类型映射与空值

- 数组类型：
  - `uint[]` / `int[]` / `int32[]` / `long[]` / `string[]` / `int[][]`
  - 直接将单元格文本包裹 `[]` 输出，故单元格内容应为逗号分隔或合规 JSON 片段，例如 `1,2,3` 或 `"a","b"`
- 数值类型：
  - `int` / `uint` / `int32` / `int64` / `long` / `float` / `double`
  - 空值导出为 `0`
- 字符串类型：
  - `string` 会进行字符转义（反斜杠与双引号），并以双引号包裹
- 其他类型：
  - 若不在上述支持列表中，会抛出异常：`不支持此类型: {type}`


### 5. c/s/cs 过滤逻辑

- 列过滤：
  - 表头第2行标记为 `c`/`s`/`cs`，导出类与 JSON 时只有匹配当前导出目标的列会被输出
- 行过滤：
  - 数据行第2列标记为 `c`/`s`/`cs`，导出 JSON 时不匹配的行会被跳过
- 默认行为：
  - 列与行的 CS 标记为空时，等同于 `cs`


### 6. 类生成与动态编译

- 类模板文件：`Share/Tool/ExcelExporter/Template.txt`
  - 模板中 `(ConfigName)` 替换为 `ProtoName`
  - 模板中 `(Fields)` 替换为按字段顺序生成的属性清单
- 类生成：
  - 针对 `c`（若表中出现过 `c`）、`s`（若表中出现过 `s`）与 `cs`，分别生成一份类文件
- 动态编译：
  - 生成的类会被分别编译为内存程序集，用于后续 JSON 反序列化与合并


### 7. JSON 与 bytes 导出

- JSON：
  - 输出到 `Config/Json/{c|s|cs}/{相对目录}/{文件名（含分片）}.txt`
  - 相对目录为 Excel 文件相对 `Unity/Assets/Config/Excel/` 的路径
- bytes：
  - 将同一 `ProtoName` 的多个 JSON（按文件名排序后逆序）依次反序列化并合并到 `{ProtoName}Category`
  - 最终写入 `Config/Excel/{c|s|cs}/{相对目录}/{ProtoName}Category.bytes`
  - 客户端会把 `Config/Excel/c` 整体复制到 `Unity/Assets/Bundles/Config`


### 8. 常见约束与建议

- 工作表名或文件名包含 `#` 会被整体跳过；临时文件 `~$xxx.xlsx` 会被跳过
- 字段名应唯一；跨文件/跨表的同一 `ProtoName` 会合并字段定义，若同名字段的列 CS 标记不一致会在日志输出提示
- `Id` 字段在 JSON 中会被导出为 `_id` 属性，同时作为条目数组的首元素（数值）参与排序/索引
- 数组/嵌套数组的单元格建议用标准 JSON 列表表达，避免额外字符导致解析歧义


### 9. 目录一览（关键路径）

- Excel 源：`Unity/Assets/Config/Excel/`
- 类输出：
  - `Unity/Assets/Scripts/Model/Generate/Client/Config`
  - `Unity/Assets/Scripts/Model/Generate/Server/Config`
  - `Unity/Assets/Scripts/Model/Generate/ClientServer/Config`
- JSON 输出：`Config/Json/{c|s|cs}/{相对目录}`
- bytes 输出：`Config/Excel/{c|s|cs}/{相对目录}`
- 客户端复制目标：`Unity/Assets/Bundles/Config`


### 10. 快速检查清单

- [ ] 文件命名是否包含正确的 `@cs/@c/@s`？若省略是否符合默认 `cs`？
- [ ] 表头 2-5 行是否按顺序配置：CS/注释/字段名/类型？
- [ ] 数据从第6行开始，第2列 CS 前缀是否正确，第3列 `Id` 是否完整？
- [ ] 数组/字符串内容格式是否满足导出器的解析与转义要求？
- [ ] 是否存在需要跳过的列/表（含 `#`）？
- [ ] 需要拆分的同一 `ProtoName` 是否使用了 `_` 分片后缀？ 