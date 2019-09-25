### AutoBuffer V0.1
Why on earth did I write this? I could have just used another serializer, but nooo I spent a whole week writing this.

This binary serializer pretty much has one of the smallest output you can have (before compression) and supports type metadata. Most of the serializers out there are bulky and do a lot more than is needed for networking. This is also pretty easy to use, and small.
What's also special about this is the type is inferred whenever possible (instead of being serialized) and no indexing of member metadata is written.

Lastly, this is geared torwards networking and games.

#### Features:
* Easy To Use
* Smallest Payload Possible with type metadata
* Easy Polymorphism and Generics
* The only metadata that's written is isNull(bit) or Type (Int16) (and Int16 for every sub generic type)
* Metadata is skipped if it can be inferred
* Metadata can be turned off for individual fields
* Indexed Field Attributes
* Or Automatically serialize via public Getters/Setters (like LiteDB)
* Custom Deserialize/Serialize functions
* Can embed size into the beginning of the byte array using an additional 0 to 3 bytes ([Header Int16, Size, Size, Size, Payload])
* Mapping a Type to another. So if the server sends a UserInfo obj I can map it to a UserInfoClientOnly obj to store client only data.
* Booleans are automatically compacted. (8 bools will only take 1 byte)
* Schema is generated at runtime
* Unity3D Support

#### Limitations:
* 16mb Write Buffer
* Needs a public parameterless constructor
* No generic class in generic classes. For example: class MyClass<T> { class myInnerClass<R> {}}
* Arrays, Lists, and Dictionarys are limited to 65,535 elements (To be fixed)
* Limited to 8191 types (except for generic subtypes). If you want to use bits in the type to determine the size of your payload.
* Datetime locality is removed (DateTime and TimeSpan is serialized as seconds(long) and nanoseconds(int) from unix epoch)
* Only supports UTF8 strings
* Does not maintain shared references (a new instance will be made for each deserialized member)
  * It's possible to work around this
* No versioning or any 'extras'
* Will throw if it tries to serialize a type without a mapping. Such as when you use a child type for a parent type.
  * I left this in so it's easy to find classes without mappings.
* Relies on CSharp-Library which I know some people won't like.
* This is pretty much the only documentation

#### Notes:
* The Int16 Header: This serializer always starts with a ushort of the type, and is designed to allow you to embed size bit flags into this header. 
  * If you limit the type id to numbers no bigger than 8191 then you will have 2 bits in the header to specify how many additional bytes to read to find the length of your payload.
  * 0 Flags == 0 More bytes of data to read. 2 Flags == 2 more bytes which could read as 0xFFFF which will mean the payload is ‭65535‬ bytes longer then what you've already read.
  * So your socket code will go like this -> Read 2 Bytes. Extract Size Length. Read 1*NumOfFlags bytes. Convert Bytes to size. Read X (size) Bytes (payload);
* If you wanna go smaller check these out:
  * https://github.com/strigeus/ipzip
  * https://github.com/centaurean/density
* Other serializers to consider:
  * https://github.com/neuecc/ZeroFormatter  //Great for random access data, and data storage.
  * https://github.com/mgravell/protobuf-net

#### How does this compare to Protobuf-net?
 * Serialization and deserialization speed is 3x-4x faster in Protobuf-net. I have yet to do any profiling and optimizations for AutoBuffer.
 * If there are any issues you can always use custom serialization methods. With protobuf you need to jump through hoops (use a surrogate type; ect).
 * Protobuf has a bigger payload (size) in most cases.
 * With Protobuf you need to know the type of your data to deserialize it.
 * Protobuf has more class decorating attributes which can get annoying to look at.
   * Basically there is a lot of 'setup' while this one 'just works'
 * Protobuf writes metadata for each field except for nulls where it writes nothing. This is what results in most of the variation of size differences between protobuf and this project.
   * Protobuf-net = Each Field+Metadata is written except for nulls. (Fields are skipped)
   * AutoBuffer   = Each Field and maybe some metadata is written except for nulls which use a flag. (No fields are skipped)
 * Protobuf-net's source has a crap ton of classes and files. This has 4 short files (<400 lines) (or 7 if you included the data reader/writers).
 * Protobuf-net plans to discontinue support for old NET compilers (such as Unitys)
 * AutoBuffer is an incredibly early and undertested library compared to Protobuf-net
 * Note that I don't have and in-depth knowledge of Protobuf-net, so I maybe wrong in some cases.

```
{
  "type": "TestProj.SubPerson",
  "Id": "-1",
  "Name": null,
  "Address": {
	"type": "TestProj.Address",
	"Line1": "Flat 1",
	"Line2": "The Meadows"
  }
}
AutoBuffer   Data: 00-07-FF-FF-FF-FF-0F-0E-06-46-6C-61-74-20-31-0B-54-68-65-20-4D-65-61-64-6F-77-73
AutoBuffer   Bytes: 27
AutoBuffer   Serialization Speed (x1000000): 00:00:02.2669464 
Protobuf-net Data: 0A-00-10-FF-FF-FF-FF-FF-FF-FF-FF-FF-01-22-15-0A-06-46-6C-61-74-20-31-12-0B-54-68-65-20-4D-65-61-64-6F-77-73
Protobuf-net Bytes: 36
Protobuf-net Serialization Speed (x1000000): 00:00:01.1997632

AutoBuffer   Deserialization Speed (x1000000): 00:00:05.5971242
Protobuf-net Deserialization Speed (x1000000): 00:00:02.0666347
```

#### TODO: 
* Serializer Helper Functions for Custom Serialization Methods
* Loadable scheme from a file, write autogenerated scheme to file
	* With a scheme we can verify api compatibility between client and server
	* Idea: Use scheme to deserialize data to and from dictionaries
* Performance Tests, Comparisons, Unit Tests
* Allow to specify serialization types without using attributes
* Exceptions in Reflection class could be better
* General cleaning up
* Add option to choose between writing flat primitives (int, long, short) over variable length primitives (varInt, ect)
* Optimizations:
  * On The Fly Compression Idea: If a pattern repeats its self x times write a counter for the amount of times the pattern repeats
  * Profile and Speed up Serialization to match Protobuf-net


#### Example Mappings:
Auto Serialization via public properties
```
[AutoBufferType(1)] //All 3 public properties will be serialized. Any reordering of these properties will be reflected in the data.
public class TestClass {
	[SkipMetaData(true, true)] //No meta data will be written for Dic and will be assumed non Null. As well as the type will be derived from reflection.
	public Dictionary<int, string> Dic { get; set; }
	public StarsInMilkyWay EnumTest { get; set; }
	public ushort? Other { get; set; }

	//This will not be serialized without special flags to the serializer instance (only properties are automatically serialized)
	public int IWillNotBeSerialized = 55;
}
```

Indexed Serialization
```
[AutoBufferType(2)]
public class MyOtherTests {
	[Index(0)]//Feel free to reorder these properties and maintain compatibility with existing data
	public Dictionary<int, string> Dic { get; set; }

	[Index(1, typeof(ushort)] //Will serialize Other as ushort
	public int Other { get; set; }

	[Index(2, typeof(TestClass)] //Ignores any children classes and forces to serialize as TestClass
	public TestClass InstanceOfTC = new ChildOfTestClass();

	//No automatic serialization if indexes are assigned
	public ushort? WillNotBeSerialized { get; set; }
}
```

Custom Serialization
```
[AutoBufferType(3)] 
public class TestSerializeMethods {
	public bool DoICare;
	public int FavoriteClient;
	public int HappyClientsCount;
	public int AngryClientsCount;

	[Serialize]
	public void Serialize(AutoBuffer serializer, DataWriter writer) {
		writer.WriteBoolean(DoICare);
		if (DoICare) {
			writer.WriteUShort((ushort)FavoriteClient);
			writer.WriteInt(HappyClientsCount);
			writer.WriteInt(AngryClientsCount);
		}
	}

	[Deserialize]
	public void Deserialize(AutoBuffer serializer, DataReader reader) {
		DoICare = reader.ReadBoolean();
		if (DoICare) {
			FavoriteClient    = (int)reader.ReadUShort();
			HappyClientsCount = reader.ReadInt();
			AngryClientsCount = reader.ReadInt();
		}
	}
}
```

#### Example Serialization:
```
class Program {

    static AutoBuffer serializer = new AutoBuffer();

    static void Main(string[] args) {
        TestSerialSmall();
        Console.ReadLine();
    }

    public static void TestSerialSmall() {

        TestMessage test = new TestMessage();
        byte[] bytes = serializer.FromObject(test);
        object deserialized = serializer.ToObject(bytes);
    }
}
```

#### What is the Apache license?
The Apache license pretty much just says "do whatever you want with this, just don't sue me" but with more words. There is also a patent license and retaliation clause which is designed to prevent patents (including patent trolls) from encumbering the software project.


#### How to add to your project:

    Prerequisite Programs:
        Git

    In GIT Terminal:
        git submodule add https://github.com/izackp/C-Sharp-Library.git WhereEverYouWant_ThirdParty/CSharp-Library
        git submodule add https://github.com/izackp/AutoBuffer.git WhereEverYouWant_ThirdParty/AutoBuffer

	Then add the code to your project file.
