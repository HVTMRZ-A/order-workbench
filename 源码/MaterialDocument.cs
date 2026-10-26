using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

internal static class MaterialDocument {
    static string A(XElement e,string name){return (string)e.Attribute(name)??"";}
    static bool HasCode(string text){return Regex.IsMatch((text??"").Normalize(NormalizationForm.FormKC),@"(?<![A-Za-z0-9.])\d+(?:\.\d+){3,}(?![A-Za-z0-9.])");}
    static string OneLine(string s){return Regex.Replace(s??"",@"\s+"," ").Trim();}
    static XDocument Xml(ZipArchive zip,string path){var entry=zip.GetEntry(path);if(entry==null)throw new InvalidDataException("文档缺少必要内容："+path);if(entry.Length>12*1024*1024)throw new InvalidDataException("文档文字内容过大，请拆分后读取");using(var stream=entry.Open())using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024}))return XDocument.Load(reader);}
    static object Section(string name,List<string> lines,List<string> sources,List<string> warnings){string text=String.Join("\n",lines);if(text.Length>100000)throw new InvalidDataException(name+" 的文字超过100000字，请拆分后读取");return new{name=name,text=text,sources=sources,warnings=warnings};}
    static int Column(XElement c){int col=0;foreach(char ch in A(c,"r").ToUpperInvariant()){if(ch<'A'||ch>'Z')break;col=col*26+ch-'A'+1;}return col;}
    static List<object> Excel(ZipArchive zip,List<string> warnings){
        var strings=new List<string>();if(zip.GetEntry("xl/sharedStrings.xml")!=null){var doc=Xml(zip,"xl/sharedStrings.xml");foreach(var si in doc.Root.Elements())strings.Add(String.Concat(si.Descendants().Where(x=>x.Name.LocalName=="t"&&!x.Ancestors().Any(y=>y.Name.LocalName=="rPh")).Select(x=>x.Value)));}
        var workbook=Xml(zip,"xl/workbook.xml");var rel=Xml(zip,"xl/_rels/workbook.xml.rels").Root.Elements().Where(x=>A(x,"TargetMode")!="External").ToDictionary(x=>A(x,"Id"),x=>A(x,"Target"));var sections=new List<object>();
        foreach(var sheet in workbook.Descendants().Where(x=>x.Name.LocalName=="sheet")){
            string name=A(sheet,"name");if(A(sheet,"state")=="hidden"||A(sheet,"state")=="veryHidden"){warnings.Add("已跳过隐藏工作表："+name);continue;}
            var id=sheet.Attributes().FirstOrDefault(x=>x.Name.LocalName=="id");string target;if(id==null||!rel.TryGetValue(id.Value,out target))continue;
            var uri=new Uri(new Uri("http://local/xl/workbook.xml"),target);if(uri.Host!="local")throw new InvalidDataException("不读取外部工作表链接");string entry=Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            var xml=Xml(zip,entry);var lines=new List<string>();var sources=new List<string>();var notes=new List<string>();bool missingFormula=false;
            foreach(var row in xml.Descendants().Where(x=>x.Name.LocalName=="row")){
                var values=new List<string>();foreach(var c in row.Elements().Where(x=>x.Name.LocalName=="c").OrderBy(Column)){
                    string type=A(c,"t"),value=(string)c.Elements().FirstOrDefault(x=>x.Name.LocalName=="v")??"";
                    if(type=="s"){int index;value=Int32.TryParse(value,out index)&&index>=0&&index<strings.Count?strings[index]:"";}
                    else if(type=="inlineStr")value=String.Concat(c.Descendants().Where(x=>x.Name.LocalName=="t").Select(x=>x.Value));
                    else if(type=="e")value="";
                    if(c.Elements().Any(x=>x.Name.LocalName=="f")&&String.IsNullOrWhiteSpace(value))missingFormula=true;
                    value=OneLine(value);if(value.Length>0)values.Add(value);
                }
                string line=String.Join("\t",values);if(!HasCode(line))continue;lines.Add(line);sources.Add(name+"!第"+A(row,"r")+"行");
            }
            if(missingFormula)notes.Add("存在公式未保存计算结果；仅使用文件中已有的结果，不执行公式。");
            if(xml.Descendants().Any(x=>x.Name.LocalName=="drawing"))notes.Add("工作表图片中的文字不在读取范围内。");
            if(lines.Count==0){warnings.Add("未发现点分数字料号，已跳过："+name);continue;}
            if(lines.Count>200)throw new InvalidDataException(name+" 超过200条料号，请筛选或拆分后读取");
            sections.Add(Section(name,lines,sources,notes));if(sections.Count>50)throw new InvalidDataException("料号工作表超过50张，请拆分后读取");
        }
        return sections;
    }
    static string WordText(XElement element){string name=element.Name.LocalName;if(name=="del")return "";if(name=="t")return element.Value;if(name=="tab")return "\t";if(name=="br"||name=="cr")return "\n";string text=String.Concat(element.Elements().Select(WordText));return name=="p"?text+"\n":text;}
    static List<object> Word(ZipArchive zip,List<string> warnings){
        var document=Xml(zip,"word/document.xml");var body=document.Descendants().FirstOrDefault(x=>x.Name.LocalName=="body");if(body==null)throw new InvalidDataException("未找到Word正文");var lines=new List<string>();var sources=new List<string>();int block=0;
        foreach(var element in body.Elements()){
            block++;if(element.Name.LocalName=="tbl"){int row=0;foreach(var tr in element.Elements().Where(x=>x.Name.LocalName=="tr")){row++;string text=String.Join("\t",tr.Elements().Where(x=>x.Name.LocalName=="tc").Select(x=>OneLine(WordText(x))));if(text.Trim().Length>0){lines.Add(text);sources.Add("Word表格"+block+"第"+row+"行");}}}
            else if(element.Name.LocalName=="p"){foreach(var line in WordText(element).Replace("\r","").Split('\n'))if(line.Trim().Length>0){lines.Add(line.Trim());sources.Add("Word段落"+block);}}
        }
        if(document.Descendants().Any(x=>x.Name.LocalName=="drawing"||x.Name.LocalName=="pict"))warnings.Add("仅读取正文和表格文字；图片、扫描件不进行文字识别。");
        return new List<object>{Section("Word正文与表格",lines,sources,new List<string>())};
    }
    static string Decode(byte[] bytes){if(bytes.Length>=2&&bytes[0]==255&&bytes[1]==254)return Encoding.Unicode.GetString(bytes,2,bytes.Length-2);if(bytes.Length>=2&&bytes[0]==254&&bytes[1]==255)return Encoding.BigEndianUnicode.GetString(bytes,2,bytes.Length-2);try{return new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}catch(DecoderFallbackException){return Encoding.GetEncoding(936).GetString(bytes);}}
    internal static object Read(string base64,string filename){
        string extension=Path.GetExtension(filename??"").ToLowerInvariant();
        if(extension==".xls"||extension==".doc")throw new InvalidDataException("旧版 "+extension+" 请先在Excel/WPS/Word中另存为 "+(extension==".xls"?".xlsx":".docx")+"，也可以直接复制表格文字粘贴识别。");
        if(!new[]{".xlsx",".xlsm",".docx",".csv",".tsv",".txt"}.Contains(extension))throw new InvalidDataException("请选择 .xlsx、.xlsm、.docx、.csv、.tsv 或 .txt 文档；PDF可复制文字后粘贴识别。");
        byte[] bytes;try{bytes=Convert.FromBase64String(base64);}catch{throw new InvalidDataException("文档文件读取失败");}if(bytes.Length==0||bytes.Length>8*1024*1024)throw new InvalidDataException("请选择1字节至8 MB的文档文件");
        var warnings=new List<string>();List<object> sections;
        if(new[]{".txt",".csv",".tsv"}.Contains(extension)){string text=Decode(bytes).Replace("\r\n","\n").Replace('\r','\n');var lines=text.Split('\n').ToList();sections=new List<object>{Section("文本文档",lines,lines.Select((x,i)=>"第"+(i+1)+"行").ToList(),new List<string>())};}
        else using(var stream=new MemoryStream(bytes))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
            if(zip.Entries.Count>2048||zip.Entries.Sum(x=>x.Length)>48*1024*1024)throw new InvalidDataException("文档解压后内容过大，请去掉无关图片或工作表后重试");
            sections=extension==".docx"?Word(zip,warnings):Excel(zip,warnings);
        }
        if(sections.Count==0)throw new InvalidDataException("未找到包含点分数字料号的可见文字；图片或扫描内容请先转为文字。");
        return new{filename=Path.GetFileName(filename),sections=sections,warnings=warnings};
    }
}
