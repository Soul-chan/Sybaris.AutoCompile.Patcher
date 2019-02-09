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
		private static ConfigData m_config = null;         // �ݒ�f�[�^ Config �ŎQ�Ƃ���

		// �ݒ�f�[�^�N���X XML�ŃV���A���C�Y���ĕۑ�����
		public class ConfigData
		{
			public string CscPath = @"C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe"; // ces.exe�̃p�X
			public string AutoCompilePath = @".\UnityInjector\AutoCompile"; // AutoCompilePath �̃p�X
			public string OutPath = @".\UnityInjector"; // �o�̓p�X
			public string BaseOption = "/t:library /unsafe /o /utf8output";
			public string[] LibPath = new string[] // ���C�u�����̃p�X
			{
				@".",
				@".\UnityInjector",
				@"..\CM3D2x64_Data\Managed",
				@"..\COM3D2x64_Data\Managed",
			};
			public string[] IgnoreDll = new string[] // �Q�Ƃ��珜�O����DLL
			{
				@"Newtonsoft.Json.dll",
			};
		}

		// �ݒ�f�[�^
		public static ConfigData Config
		{
			get
			{
				if ( m_config == null )
				{
					try
					{
						// XML����ǂݍ���
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

							// �R���t�B�O�t�H���_��������΍��
							if ( !Directory.Exists( ConfigPath ) )
							{
								Directory.CreateDirectory( ConfigPath );
							}

							// XML�֏�������
							StreamWriter sw = new StreamWriter( XmlPath, false, Encoding.UTF8 );
							XmlSerializer serializer = new XmlSerializer( typeof( ConfigData ) );
							serializer.Serialize( sw, Config );
							sw.Close();
						}
						catch
						{
						}
					}

					// ��r�p�ɏ������ɂ��Ă���
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

				// csc.exe �� AutoCompile �t�H���_ �o�̓t�H���_��������ΏI��
				if ( !File.Exists( Config.CscPath ) ||
					 ! Directory.Exists( acPath ) ||
					 ! Directory.Exists( outPath ) ) { return; }

				// AutoCompile �t�H���_���̃t�@�C�����
				string errMsg = "";
				var files = CompFiles.MakeList( acPath, outPath, ref errMsg );

				// �R���p�C������t�@�C����������ΏI��
				if ( files.Count == 0 )	{ return; }

				string option = " " + Config.BaseOption;
				string reference = "";

				foreach ( var lib in Config.LibPath )
				{
					string libPath = Path.GetFullPath( BasePath + lib );

					// �Ō�ɉ~�}�[�N�����Ă���ꍇ�͏����Ă���
					libPath = libPath.TrimEnd( Path.DirectorySeparatorChar );

					if ( Directory.Exists( libPath ) )
					{
						option += " /lib:" + libPath.DQ();

						// �w�肳�ꂽlib�t�H���_����DLL���Q�ƂƂ��Ēǉ�����
						DirectoryInfo dirInfo = new DirectoryInfo( libPath );
						FileInfo[] refs = dirInfo.GetFiles( "*.dll" )
										.Where( _ => _.Name.EndsWith( "dll", StringComparison.CurrentCultureIgnoreCase ) )
										.ToArray();

						foreach ( FileInfo file in refs )
						{
							// ���O����DLL�łȂ���Βǉ�
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

				// �������g�̃o�[�W���������擾���ăv�����g
				FileVersionInfo ver = FileVersionInfo.GetVersionInfo( Assembly.GetExecutingAssembly().Location );

				logSw.WriteLine( ver.FileDescription + " " + ver.FileVersion );
				logSw.WriteLine( "" );

				// ���\�[�X�ϊ����̃G���[���b�Z�[�W������Ε\�����Ă���
				if ( ! string.IsNullOrEmpty(errMsg) )
				{
					logSw.WriteLine( errMsg );
				}

				// �R���p�C���Ώۂ̃t�@�C�������[�v
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

					// �r���h�R�}���h�����O�֏o��
					logSw.WriteLine( "---------------------------------------------" );
					logSw.WriteLine( info.FileName + info.Arguments );
					logSw.WriteLine( "" );

					// �R���p�C�����s
					Process p = Process.Start( info );
					string result = p.StandardOutput.ReadToEnd();
					p.WaitForExit();
					p.Close();

					// ���ʂ����O�֏o��
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

		// resx �t�@�C�����瓯�t�H���_�� resources �t�@�C�������
		public static bool resx2resources( string resxName, ref string resourcesName, ref string errMsg )
		{
			try
			{
				resourcesName = getResourcesName( resxName );

				if ( string.IsNullOrEmpty( resourcesName ) ) { return false; }
				// ���ɂ���ꍇ�͉������������ŕԂ�
				if ( File.Exists( resourcesName ) ) { return true; }

				ResXResourceReader reader = new ResXResourceReader( resxName );
				ResourceWriter writer = new ResourceWriter( resourcesName );
				reader.BasePath = Path.GetDirectoryName( resxName );

				// ���\�[�X�����ׂė񋓂��Ēǉ�
				foreach ( DictionaryEntry entry in reader )
				{
					writer.AddResource( (string)entry.Key, entry.Value );
				}

				// ����
				reader.Close();
				writer.Close();

				// ����
				return true;
			}
			catch( Exception ex )
			{
				errMsg += resxName + " ���� " + resourcesName + " �ւ̕ϊ��Ɏ��s���܂����B\n";
				errMsg += ex.Message + "\n";

				resourcesName = "";
			}

			return false;
		}

		// resx �ɕt������ *.Designer.cs �t�@�C������ resources �t�@�C���̖��O��T���č��
		private static string getResourcesName( string resxName )
        {
			// ResourceManager �̑�1�����ɗ^�����Ă��镶���񂪕K�v��resources�̖��O
			Regex rgx = new Regex( @"Resources\.ResourceManager\(""(.+)""" );
			string designerName = Path.ChangeExtension( resxName, ".Designer.cs" );

			// *.Designer.cs �̒����P�s������
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

	// �R���p�C������t�@�C��
	class CompFiles
	{
		public string In { get; private set; }
		public string Out { get; private set; }
		public string Res { get; private set; }

		//
		public static List<CompFiles> MakeList( string acPath, string outPath, ref string errMsg )
		{
			List<CompFiles> list = new List<CompFiles>();

			// AutoCompile �t�H���_���̃t�@�C�����
			DirectoryInfo dirInfo = new DirectoryInfo( acPath );
			FileInfo[] files = dirInfo.GetFiles( "*.cs" );
			DirectoryInfo[] dirs = dirInfo.GetDirectories();

			// �R���p�C������t�@�C�����t�H���_��������ΏI��
			if ( files.Length == 0 && dirs.Length == 0 ) { return list; }

			// �R���p�C���Ώۂ̃t�@�C�������[�v
			foreach ( FileInfo file in files )
			{
				// �o�̓t�@�C���������
				string outfile = Path.GetFullPath( outPath + @"\" + Path.ChangeExtension( file.Name, ".dll" ) );

				// ���ɑ��݂��Ă���ꍇ�̓R���p�C�����Ȃ�
				if ( File.Exists( outfile ) ) { continue; }

				list.Add( new CompFiles
				{
					In = " " + file.FullName.DQ(),
					Out = " /out:" + outfile.DQ(),
					Res = ""
				} );
			}

			// �t�H���_�� recurse �I�v�V������DLL���o����悤�ɂ���
			foreach ( DirectoryInfo dir in dirs )
			{
				// �o�̓t�@�C���������
				string outfile = Path.GetFullPath( outPath + @"\" + dir.Name + ".dll" );

				// ���ɑ��݂��Ă���ꍇ�̓R���p�C�����Ȃ�
				if ( File.Exists( outfile ) ) { continue; }

				// �t�H���_���� *.cs �t�@�C����������Ύ���
				var csFiles = dir.GetFiles( "*.cs", SearchOption.AllDirectories );
				if ( csFiles.Length == 0 ) { continue; }

				// �t�H���_���� *.resources �t�@�C��������΁A��������\�[�X�Ƃ��Ďg��
				var resFiles =
				dir.GetFiles( "*.resources", SearchOption.AllDirectories )
				.Select( _ => "/res:" + _.FullName.DQ() )
				.ToList();

				// *.resources �t�@�C�������� *.resx ������Ȃ�Aresx �� resources �ɕϊ����Ă�����g��
				if ( resFiles.Count() == 0)
				{
					// �t�H���_���� *.resx �t�@�C�����
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