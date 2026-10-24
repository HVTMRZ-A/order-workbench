using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;

internal static class ProductionPreview {
    static XNamespace m="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static XNamespace rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    class Cell {public int Row,Col;public string Ref,Value; public bool MissingFormula;}
    static string Clean(string value){return Regex.Replace(value??"",@"\s+"," ").Trim();}
    static string Label(string value){return Regex.Replace(value??"",@"[\s:：]","");}
    static string A(XElement x,string name){return (string)x.Attribute(name)??"";}
    static XDocument Xml(ZipArchive zip,string name){var entry=zip.GetEntry(name);if(entry==null)throw new InvalidDataException("生产单缺少必要内容："+name);if(entry.Length>12*1024*1024)throw new InvalidDataException("生产单内容过大");using(var s=entry.Open())using(var reader=XmlReader.Create(s,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024}))return XDocument.Load(reader);}
    static int Col(string address){int result=0;foreach(char c in address){if(c>='A'&&c<='Z')result=result*26+c-'A'+1;else if(c>='a'&&c<='z')result=result*26+c-'a'+1;else break;}return result;}
    static string TextAt(List<Cell> cells,int row,int col){var c=cells.FirstOrDefault(x=>x.Row==row&&x.Col==col);return c==null?"":c.Value;}
    static Cell Find(List<Cell> cells,int before,params string[] keys){foreach(var c in cells.OrderBy(x=>x.Row).ThenBy(x=>x.Col)){if(before>0&&c.Row>=before)continue;if(!keys.Contains(Label(c.Value)))continue;var value=cells.Where(x=>x.Row==c.Row&&x.Col>c.Col&&x.Value.Trim().Length>0).OrderBy(x=>x.Col).FirstOrDefault();if(value!=null)return value;}return null;}
    static string V(Cell c){return c==null?"":Clean(c.Value);}
    static string CellRef(Cell c){return c==null?"":c.Ref;}
    static string DateValue(Cell c,bool date1904,List<string>warnings){string text=V(c);if(text=="")return "";double num;if(Double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out num)&&num>=20000&&num<=100000)return (date1904?new DateTime(1904,1,1).AddDays(num):DateTime.FromOADate(num)).ToString("yyyy-MM-dd");DateTime date;if(DateTime.TryParseExact(text,new[]{"yyyy-MM-dd","yyyy/M/d","yyyy年M月d日","yyyy.MM.dd","yyyy/M/d H:mm:ss","yyyy-MM-ddTHH:mm:ss"},CultureInfo.InvariantCulture,DateTimeStyles.None,out date))return date.ToString("yyyy-MM-dd");warnings.Add("日期未能明确识别（"+CellRef(c)+"）："+text+"，请手动填写。");return "";}
    static Dictionary<string,object> Field(string value,Cell cell){return new Dictionary<string,object>{{"value",value},{"cell",CellRef(cell)}};}
    public static object Read(string base64,string filename){
        if(!filename.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("请选择 .xlsx 生产单；旧版 .xls 请先在 Excel 或 WPS 中另存为 .xlsx。");
        byte[] bytes;try{bytes=Convert.FromBase64String(base64);}catch{throw new InvalidDataException("生产单文件读取失败");}if(bytes.Length>8*1024*1024)throw new InvalidDataException("生产单文件不能超过 8 MB");
        string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        using(var stream=new MemoryStream(bytes))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
            if(zip.Entries.Count>1024||zip.Entries.Sum(x=>x.Length)>48*1024*1024)throw new InvalidDataException("生产单内容过大，请去掉无关图片或工作表后重试。");
            var strings=new List<string>();if(zip.GetEntry("xl/sharedStrings.xml")!=null)foreach(var si in Xml(zip,"xl/sharedStrings.xml").Root.Elements(m+"si"))strings.Add(String.Concat(si.Descendants(m+"t").Select(t=>t.Value)));
            var wb=Xml(zip,"xl/workbook.xml");var properties=wb.Root.Element(m+"workbookPr");bool date1904=properties!=null&&(A(properties,"date1904")=="1"||A(properties,"date1904")=="true");
            XNamespace packageRel="http://schemas.openxmlformats.org/package/2006/relationships";var relationships=Xml(zip,"xl/_rels/workbook.xml.rels").Root.Elements(packageRel+"Relationship").Where(x=>A(x,"TargetMode")!="External").ToDictionary(x=>A(x,"Id"),x=>A(x,"Target"));
            var results=new List<object>();var skipped=new List<string>();
            foreach(var sheet in wb.Root.Element(m+"sheets").Elements(m+"sheet")){
                string sheetName=A(sheet,"name");if(A(sheet,"state")=="hidden"||A(sheet,"state")=="veryHidden"){skipped.Add(sheetName+"（隐藏工作表）");continue;}
                string target; if(!relationships.TryGetValue((string)sheet.Attribute(rel+"id"),out target))continue;
                var uri=new Uri(new Uri("http://local/xl/workbook.xml"),target);string entryPath=Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));var xml=Xml(zip,entryPath);var cells=new List<Cell>();var warnings=new List<string>();
                foreach(var row in xml.Descendants(m+"row"))foreach(var cell in row.Elements(m+"c")){
                    string address=A(cell,"r");var match=Regex.Match(address,@"^(\D+)(\d+)$");if(!match.Success)continue;int rowNum=Int32.Parse(match.Groups[2].Value);string type=A(cell,"t"),value=(string)cell.Element(m+"v")??"";bool formula=cell.Element(m+"f")!=null;
                    if(type=="s"){int idx;if(Int32.TryParse(value,out idx)&&idx>=0&&idx<strings.Count)value=strings[idx];else value="";}else if(type=="inlineStr")value=String.Concat(cell.Descendants(m+"t").Select(t=>t.Value));
                    if(type=="e")value="";cells.Add(new Cell{Ref=address,Row=rowNum,Col=Col(address),Value=value,MissingFormula=formula&&String.IsNullOrWhiteSpace(value)});
                }
                var header=cells.GroupBy(x=>x.Row).OrderBy(x=>x.Key).FirstOrDefault(g=>g.Any(c=>Label(c.Value)=="数量")&&g.Any(c=>Label(c.Value)=="公司型号"||Label(c.Value)=="客户型号"));
                if(header==null){skipped.Add(sheetName+"（未找到公司型号 / 客户型号及数量表头）");continue;}
                int hr=header.Key;var columns=new Dictionary<string,int>();foreach(var c in header)columns[Label(c.Value)]=c.Col;
                Func<int,string[],Cell> get=delegate(int row,string[] keys){int col=0;foreach(string key in keys)if(columns.TryGetValue(key,out col))return cells.FirstOrDefault(c=>c.Row==row&&c.Col==col);return null;};
                var contract=Find(cells,hr,"合同编号","订单编号","订单号");var customer=Find(cells,hr,"客户名称","客户");var customerCode=Find(cells,hr,"客户编号","客户代码");var total=Find(cells,hr,"数量","总数量");var due=Find(cells,hr,"计划完成日期","交货日期","交期");var transport=Find(cells,hr,"运输方式");var property=Find(cells,hr,"订单属性");var operatorCell=Find(cells,0,"经办人");var orderDate=Find(cells,0,"下单日期");var noteCell=Find(cells,0,"注意事项","备注");
                var fields=new Dictionary<string,object>{{"contractNumber",Field(V(contract),contract)},{"customer",Field(V(customer),customer)},{"customerCode",Field(V(customerCode),customerCode)},{"due",Field(DateValue(due,date1904,warnings),due)},{"headerQuantity",Field(V(total),total)},{"transport",Field(V(transport),transport)},{"orderProperty",Field(V(property),property)},{"sourceOperator",Field(V(operatorCell),operatorCell)},{"orderDate",Field(DateValue(orderDate,date1904,warnings),orderDate)},{"notes",Field(noteCell==null?"":noteCell.Value.Trim(),noteCell)}};
                var rows=new List<Dictionary<string,object>>();double sum=0;
                foreach(int r in cells.Where(x=>x.Row>hr).Select(x=>x.Row).Distinct().OrderBy(x=>x)){
                    if(cells.Any(x=>x.Row==r&&x.Col<=2&&new[]{"注意事项","合计","复核","批准","备注"}.Contains(Label(x.Value))))break;
                    Cell company=get(r,new[]{"公司型号"}),client=get(r,new[]{"客户型号"}),qty=get(r,new[]{"数量"});if(V(company)==""&&V(client)=="")continue;
                    string companyModel=V(company),customerModel=V(client);double count;bool valid=Double.TryParse(V(qty),NumberStyles.Number,CultureInfo.InvariantCulture,out count)&&count>0&&count<=100000000&&Math.Floor(count)==count;
                    if(!valid){warnings.Add("第 "+r+" 行数量缺失或不是正整数，请核对后填写。");count=0;}sum+=count;
                    Cell requirement=get(r,new[]{"产品要求","技术要求"}),packaging=get(r,new[]{"包材","包材要求"}),material=get(r,new[]{"物料代码","物料编码","料号"}),serial=get(r,new[]{"序号"}),category=get(r,new[]{"产品大类","产品类别"}),unit=get(r,new[]{"单位"}),bom=get(r,new[]{"BOM状态"});
                    var refs=new Dictionary<string,string>();foreach(var item in new[]{new KeyValuePair<string,Cell>("model",company??client),new KeyValuePair<string,Cell>("customerModel",client),new KeyValuePair<string,Cell>("quantity",qty),new KeyValuePair<string,Cell>("productRequirements",requirement),new KeyValuePair<string,Cell>("packagingRequirements",packaging),new KeyValuePair<string,Cell>("machineCode",material)})refs[item.Key]=CellRef(item.Value);
                    rows.Add(new Dictionary<string,object>{{"row",r},{"serial",V(serial)},{"model",companyModel!=""?companyModel:customerModel},{"rawCompanyModel",company==null?"":company.Value},{"customerModel",customerModel},{"quantity",count},{"category",V(category)},{"unit",V(unit)},{"productRequirements",requirement==null?"":requirement.Value.Trim()},{"packagingRequirements",packaging==null?"":packaging.Value.Trim()},{"machineCode",V(material)},{"bomStatus",V(bom)},{"cells",refs}});
                }
                if(rows.Count==0){skipped.Add(sheetName+"（没有可识别的产品明细）");continue;}if(rows.Count>500)throw new InvalidDataException("单张工作表超过 500 行，请拆分后导入。");
                double headerCount;if(Double.TryParse(V(total),NumberStyles.Number,CultureInfo.InvariantCulture,out headerCount)&&headerCount!=sum)warnings.Add("数量不一致：表头 "+CellRef(total)+" 为 "+headerCount+"，明细合计为 "+sum+"。本次按逐行数量导入，请核对。");
                if(V(contract)=="")warnings.Add("未识别到合同编号，请在导入前填写。");if(V(customer)==""&&V(customerCode)=="")warnings.Add("未识别到客户名称或编号，请填写。");if(V(customer)==""&&V(customerCode)!="")warnings.Add("表中仅有客户编号，将先以编号作为客户标识，可在预览中补全名称。");
                if(xml.Descendants(m+"drawing").Any()||xml.Descendants(m+"legacyDrawing").Any())warnings.Add("工作表含图片或绘图；本版读取单元格文字，图片内容请另行核对。");
                if(cells.Any(c=>c.MissingFormula))warnings.Add("工作表中有公式缺少保存结果，相关空白字段请核对；程序不会执行公式。");
                results.Add(new Dictionary<string,object>{{"sheet",sheetName},{"headerRow",hr},{"fields",fields},{"items",rows},{"detailTotal",sum},{"warnings",warnings}});
            }
            if(results.Count==0)throw new InvalidDataException("未找到可识别的生产单明细。需包含公司型号 / 客户型号、数量等表头。");
            return new Dictionary<string,object>{{"filename",Path.GetFileName(filename)},{"sha256",hash},{"sheets",results},{"skipped",skipped}};
        }
    }
}
