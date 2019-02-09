using System;
using Mono.Cecil;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Resources;
using System.Collections;
using System.Text.RegularExpressions;

[assembly: AssemblyVersion( "1.0.0.0" )]
[assembly: AssemblyTitle( "Sybaris.AutoCompile.Patcher" )]
namespace Sybaris.AutoCompile.Patcher
{
	public static class AutoCompilePatch
	{
		public static readonly string[] TargetAssemblyNames = { "Assembly-CSharp.dll" };
		private static readonly string BasePath = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location ) + @"\";
		private static readonly string ConfigPath = BasePath + "Config";
		private static readonly string XmlPath = ConfigPath + @"\" + Assembly.GetExecutingAssembly().GetName().Name + ".xml";
		private static ConfigData m_config = null;         // 設定データ Config で参照する

		// 設定データクラス XMLでシリアライズして保存する
		public class ConfigData
		{
			public string CscPath = @"C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe"; // ces.exeのパス
			public string AutoCompilePath = @".\UnityInjector\AutoCompile"; // AutoCompilePath のパス
			public string OutPath = @".\UnityInjector"; // 出力パス
			public string BaseOption = "/t:library /unsafe /o /utf8output";
			public string[] LibPath = new string[] // ライブラリのパス
			{
				@".",
				@".\UnityInjector",
				@"..\CM3D2x64_Data\Managed",
				@"..\COM3D2x64_Data\Managed",
			};
			public string[] IgnoreDll = new string[] // 参照から除外するDLL
			{
				@"Newtonsoft.Json.dll",
			};
		}

		// 設定データ
		public static ConfigData Config
		{
			get
			{
				if ( m_config == null )
				{
					try
					{
						// XMLから読み込み
						StreamReader sr = new StreamReader( XmlPath, Encoding.UTF8 );
						XmlSerializer serializer = new XmlSerializer( typeof( ConfigData ) );
						m_config = (ConfigData)serializer.Deserialize( sr );
						sr.Close();
					}
					catch
					{
						try
						{
							m_config = new ConfigData();

							// コンフィグフォルダが無ければ作る
							if ( !Directory.Exists( ConfigPath ) )
							{
								Directory.CreateDirectory( ConfigPath );
							}

							// XMLへ書き込み
							StreamWriter sw = new StreamWriter( XmlPath, false, Encoding.UTF8 );
							XmlSerializer serializer = new XmlSerializer( typeof( ConfigData ) );
							serializer.Serialize( sw, Config );
							sw.Close();
						}
						catch
						{
						}
					}

					// 比較用に小文字にしておく
					for ( int cnt = 0; cnt < m_config.IgnoreDll.Length; cnt++ )
					{
						m_config.IgnoreDll[cnt] = m_config.IgnoreDll[cnt].ToLower();
					}
				}

				return m_config;
			}
		}
		static AutoCompilePatch()
		{
		}

		public static void Patch(AssemblyDefinition assembly)
		{
			try
			{
				string acPath = Path.GetFullPath( BasePath + Config.AutoCompilePath );
				string outPath = Path.GetFullPath( BasePath + Config.OutPath );

				// csc.exe や AutoCompile フォルダ 出力フォルダが無ければ終了
				if ( !File.Exists( Config.CscPath ) ||
					 ! Directory.Exists( acPath ) ||
					 ! Directory.Exists( outPath ) ) { return; }

				// AutoCompile フォルダ内のファイルを列挙
				string errMsg = "";
				var files = CompFiles.MakeList( acPath, outPath, ref errMsg );

				// コンパイルするファイルが無ければ終了
				if ( files.Count == 0 )	{ return; }

				string option = " " + Config.BaseOption;
				string reference = "";

				foreach ( var lib in Config.LibPath )
				{
					string libPath = Path.GetFullPath( BasePath + lib );

					// 最後に円マークがついている場合は消しておく
					libPath = libPath.TrimEnd( Path.DirectorySeparatorChar );

					if ( Directory.Exists( libPath ) )
					{
						option += " /lib:" + libPath.DQ();

						// 指定されたlibフォルダ内のDLLを参照として追加する
						DirectoryInfo dirInfo = new DirectoryInfo( libPath );
						FileInfo[] refs = dirInfo.GetFiles( "*.dll" )
										.Where( _ => _.Name.EndsWith( "dll", StringComparison.CurrentCultureIgnoreCase ) )
										.ToArray();

						foreach ( FileInfo file in refs )
						{
							// 除外するDLLでなければ追加
							if ( ! Config.IgnoreDll.Contains( file.Name.ToLower() ) )
							{
								reference += " /r:" + file.Name.DQ();
							}
						}
					}
				}
				option += reference;
			
				string logFile = acPath + @"\AutoCompile.log";
				StreamWriter logSw = new StreamWriter( logFile, false, Encoding.UTF8 );

				// 自分自身のバージョン情報を取得してプリント
				FileVersionInfo ver = FileVersionInfo.GetVersionInfo( Assembly.GetExecutingAssembly().Location );

				logSw.WriteLine( ver.FileDescription + " " + ver.FileVersion );
				logSw.WriteLine( "" );

				// リソース変換時のエラーメッセージがあれば表示しておく
				if ( ! string.IsNullOrEmpty(errMsg) )
				{
					logSw.WriteLine( errMsg );
				}

				// コンパイル対象のファイル分ループ
				foreach ( var file in files )
				{
					ProcessStartInfo info = new ProcessStartInfo()
					{
						FileName = Config.CscPath,
						Arguments = option + file.Res + file.Out + file.In,
						CreateNoWindow = true,
						UseShellExecute = false,
						RedirectStandardOutput = true
					};

					// ビルドコマンドをログへ出力
					logSw.WriteLine( "---------------------------------------------" );
					logSw.WriteLine( info.FileName + info.Arguments );
					logSw.WriteLine( "" );

					// コンパイル実行
					Process p = Process.Start( info );
					string result = p.StandardOutput.ReadToEnd();
					p.WaitForExit();
					p.Close();

					// 結果をログへ出力
					logSw.WriteLine( result );
					logSw.WriteLine( "" );
				}

				logSw.Close();
			}
			catch
		//	( Exception ex )
			{
		//		MessageBox.Show( ex.Message, "" );
			}
		}

		// resx ファイルから同フォルダに resources ファイルを作る
		public static bool resx2resources( string resxName, ref string resourcesName, ref string errMsg )
		{
			try
			{
				resourcesName = getResourcesName( resxName );

				if ( string.IsNullOrEmpty( resourcesName ) ) { return false; }
				// 既にある場合は何もせず成功で返る
				if ( File.Exists( resourcesName ) ) { return true; }

				ResXResourceReader reader = new ResXResourceReader( resxName );
				ResourceWriter writer = new ResourceWriter( resourcesName );
				reader.BasePath = Path.GetDirectoryName( resxName );

				// リソースをすべて列挙して追加
				foreach ( DictionaryEntry entry in reader )
				{
					writer.AddResource( (string)entry.Key, entry.Value );
				}

				// 閉じる
				reader.Close();
				writer.Close();

				// 成功
				return true;
			}
			catch( Exception ex )
			{
				errMsg += resxName + " から " + resourcesName + " への変換に失敗しました。\n";
				errMsg += ex.Message + "\n";

				resourcesName = "";
			}

			return false;
		}

		// resx に付随する *.Designer.cs ファイルから resources ファイルの名前を探して作る
		private static string getResourcesName( string resxName )
        {
			// ResourceManager の第1引数に与えられている文字列が必要なresourcesの名前
			Regex rgx = new Regex( @"Resources\.ResourceManager\(""(.+)""" );
			string designerName = Path.ChangeExtension( resxName, ".Designer.cs" );

			// *.Designer.cs の中を１行ずつ検索
			using ( StreamReader file = new StreamReader( designerName, Encoding.UTF8 ) )
			{
				string line = "";

				while ( (line = file.ReadLine()) != null )
				{
					Match match = rgx.Match( line );
					if ( match.Success )
					{
						return Path.GetDirectoryName( resxName ) + @"\" + match.Groups[ 1 ].Value + ".resources";
					}
				}
			}

			return "";
		}
	}

	// コンパイルするファイル
	class CompFiles
	{
		public string In { get; private set; }
		public string Out { get; private set; }
		public string Res { get; private set; }

		//
		public static List<CompFiles> MakeList( string acPath, string outPath, ref string errMsg )
		{
			List<CompFiles> list = new List<CompFiles>();

			// AutoCompile フォルダ内のファイルを列挙
			DirectoryInfo dirInfo = new DirectoryInfo( acPath );
			FileInfo[] files = dirInfo.GetFiles( "*.cs" );
			DirectoryInfo[] dirs = dirInfo.GetDirectories();

			// コンパイルするファイルもフォルダも無ければ終了
			if ( files.Length == 0 && dirs.Length == 0 ) { return list; }

			// コンパイル対象のファイル分ループ
			foreach ( FileInfo file in files )
			{
				// 出力ファイル名を作る
				string outfile = Path.GetFullPath( outPath + @"\" + Path.ChangeExtension( file.Name, ".dll" ) );

				// 既に存在している場合はコンパイルしない
				if ( File.Exists( outfile ) ) { continue; }

				list.Add( new CompFiles
				{
					In = " " + file.FullName.DQ(),
					Out = " /out:" + outfile.DQ(),
					Res = ""
				} );
			}

			// フォルダは recurse オプションでDLLが出来るようにする
			foreach ( DirectoryInfo dir in dirs )
			{
				// 出力ファイル名を作る
				string outfile = Path.GetFullPath( outPath + @"\" + dir.Name + ".dll" );

				// 既に存在している場合はコンパイルしない
				if ( File.Exists( outfile ) ) { continue; }

				// フォルダ内に *.cs ファイルが無ければ次へ
				var csFiles = dir.GetFiles( "*.cs", SearchOption.AllDirectories );
				if ( csFiles.Length == 0 ) { continue; }

				// フォルダ内に *.resources ファイルがあれば、それをリソースとして使う
				var resFiles =
				dir.GetFiles( "*.resources", SearchOption.AllDirectories )
				.Select( _ => "/res:" + _.FullName.DQ() )
				.ToList();

				// *.resources ファイルが無く *.resx があるなら、resx を resources に変換してそれを使う
				if ( resFiles.Count() == 0)
				{
					// フォルダ内の *.resx ファイルを列挙
					var resxFiles = dir.GetFiles( "*.resx", SearchOption.AllDirectories );
					foreach ( var resx in resxFiles )
					{
						string resourcesName = "";

						if ( AutoCompilePatch.resx2resources( resx.FullName, ref resourcesName, ref errMsg ) )
						{
							resFiles.Add( "/res:" + resourcesName.DQ() );
						}
					}
				}

				list.Add( new CompFiles
				{
					In = " /recurse:" + (dir.FullName + @"\*.cs").DQ(),
					Out = " /out:" + outfile.DQ(),
					Res = " " + string.Join( " ", resFiles.ToArray() )
				} );
			}
			return list;
		}
	}

	static class Extensions
	{
		public static string DQ( this string str )
		{
			return "\"" + str + "\"";
		}
	}
}