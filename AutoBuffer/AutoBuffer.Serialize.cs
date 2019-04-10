using CSharp_Library.Utility;
using CSharp_Library.Utility.Data;
using System;
using System.Collections;
using System.Collections.Generic;

namespace AutoBuffer {
    public partial class Serializer {
        DataWriter _writer = new DataWriter();

        DataWriter GetDataWriter() {
            if (_writer == null)
                _writer = new DataWriter();
            return _writer;
        }

        public void ClearWriteCache() {
            _writer = null;
        }

        /// <summary>
        /// Serializes obj to byte[]. Passing Type can be used to avoid prefixing type to beginning of data. Important to match type parameter on deserialization.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="obj"></param>
        /// <returns></returns>
        public byte[] FromObject(object obj, Type type = null, bool prefixTypeData = true) {
            if (obj == null)
                throw new ArgumentNullException("obj");
            if (type == null)
                type = typeof(object);

            DataWriter writer = GetDataWriter();
            writer.Reset();
            if (prefixTypeData) {
                type = obj.GetType(); //Has to match because whatever we prefix we will try to deserialize //TODO: Double check
                TypeCache typeCache = GetTypeCache(type);
                WriteType(type, obj, writer, typeCache.IsGeneric, true);
                Serialize(type, obj, writer, true, true);
            } else {
                Serialize(type, obj, writer, false, true);
            }
            return writer.CopyBytes();
        }

        void WriteType(Type type, object obj, DataWriter writer, bool isGeneric, bool WriteFullHeader = false) {

            if (obj == null) {
                if (WriteFullHeader)
                    writer.WriteUShort(0);
                else
                    writer.WriteVarNum(0);
                return;
            }

            {
                ushort header = HeaderForType(type, isGeneric);
                if (WriteFullHeader)
                    writer.WriteUShort(header);
                else
                    writer.WriteVarNum(header);
            }

            if (isGeneric) {
                Type[] genericArguments = type.GetGenericArguments();

                for (int i = 0; i < genericArguments.Length; i += 1) {
                    ushort header = HeaderForType(genericArguments[i], isGeneric);
                    writer.WriteVarNum(header);
                }
            }
        }
        
        //We need to pass the original field type. If the type is object, interface or has children then the metadata needs to be written
        public void Serialize(Type fieldType, object obj, DataWriter writer, bool skipMetaData = false, bool skipNullByte = false) {
            
            if (obj == null && skipNullByte) {
                throw new Exception("skipNullByte option is set AND object to be written is null. No way to deserialize since deserializer will assume non-null value.");
            }

            Type type = (obj != null) ? obj.GetType() : null;
            TypeCache typeCache = GetTypeCache(type);
            TypeCache fieldCache = GetTypeCache(fieldType);

            if (skipNullByte == false && fieldCache.CanBeNull()) {
                writer.WriteBoolean((obj == null));
            }

            if (obj == null)
                return;

            if (skipMetaData == false && fieldCache.HasChildTypes) {
                WriteType(type, obj, writer, typeCache.IsGeneric);
            }

            if (typeCache.IsNullable) {
                Type[] types = Reflection.GetGenericArgumentsExt(type);
                Serialize(types[0], obj, writer);
                return;
            }

            if (type.IsPrimitive) {
                SerializePrimitive(type, obj, writer);
                return;
            }

            if (type == typeof(String)) {
                writer.WriteUTF((string)obj);
                return;
            }

            if (type == typeof(Guid)) {
                writer.WriteBytes(((Guid)obj).ToByteArray());
                return;
            }

            if (type.IsEnum) {
                Type subType = Enum.GetUnderlyingType(type);
                SerializePrimitive(subType, obj, writer);
                return;
            }

            if (typeCache.IsList) {
                if (type == typeof(object)) //TODO: I don't get this
                    SerializeArray(type, obj, writer);
                else if (type.IsArray)
                    SerializeArray(type.GetElementType(), obj, writer);
                else if (typeof(IList<>).IsAssignableFrom(type))
                {
                    Type[] genericArguments = Reflection.GetGenericArgumentsExt(type);
                    SerializeIEnumerable(genericArguments[0], obj as IList, writer);
                }
                else if (typeof(IEnumerable<>).IsAssignableFrom(type))
                {
                    Type[] genericArguments = Reflection.GetGenericArgumentsExt(type);
                    SerializeIEnumerable(genericArguments[0], obj as IEnumerable, writer);
                }
                else if (typeof(IList).IsAssignableFrom(type))
                {
                    var list = obj as IList;
                    SerializeIList(typeof(object), list, writer);
                }
                else if (typeof(IEnumerable).IsAssignableFrom(type))
                    SerializeIEnumerable(typeof(object), obj as IEnumerable, writer);
                else
                    throw new Exception("Unexpected list type: " + type.ToString());
                return;
            }

            if (typeCache.IsGeneric && obj is IDictionary) {
                Type[] genericArguments = Reflection.GetGenericArgumentsExt(type);
                Type k = genericArguments[0];
                Type t = genericArguments[1];
                SerializeDictionary(k, t, (IDictionary)obj, writer);
                return;
            }

            SerializeObject(type, obj, writer);
        }

        void SerializePrimitive(Type type, object obj, DataWriter writer) {
            if (type == typeof(Int16)) {
                writer.WriteVarShort((ushort)(Int16)obj);
                return;
            }

            if (type == typeof(Int32)) {
                writer.WriteVarInt((uint)(Int32)obj);
                return;
            }

            if (type == typeof(Int64)) {
                writer.WriteVarNum((ulong)(Int64)obj);
                return;
            }

            if (type == typeof(UInt16)) {
                writer.WriteVarShort((UInt16)obj);
                return;
            }

            if (type == typeof(UInt32)) {
                writer.WriteVarInt((UInt32)obj);
                return;
            }

            if (type == typeof(UInt64)) {
                writer.WriteVarNum((UInt64)obj);
                return;
            }

            if (type == typeof(float)) {
                writer.WriteVarInt((uint)(float)obj);
                return;
            }

            if (type == typeof(Double)) {
                writer.WriteVarNum((ulong)(Double)obj);
                return;
            }

            if (type == typeof(char)) {
                writer.WriteChar((char)obj);
                return;
            }

            if (type == typeof(byte)) {
                writer.WriteByte((byte)obj);
                return;
            }

            if (type == typeof(sbyte)) {
                writer.WriteSByte((sbyte)obj);
                return;
            }

            if (type == typeof(Boolean)) {
                writer.WriteBoolean((Boolean)obj);
                return;
            }

            if (type == typeof(Decimal)) {
                writer.WriteDecimal((Decimal)obj);
                return;
            }

            if (type == typeof(DateTime)) {
                DateTime dateTime = (DateTime)obj;
                dateTime = dateTime.ToUniversalTime();

                long secondsSinceBclEpoch = dateTime.Ticks / TimeSpan.TicksPerSecond;
                int nanoseconds = (int)(dateTime.Ticks % TimeSpan.TicksPerSecond) * Duration.NanosecondsPerTick;

                writer.WriteVarNum((ulong)(secondsSinceBclEpoch - Timestamp.BclSecondsAtUnixEpoch));
                writer.WriteVarInt((uint)(nanoseconds));
                return;
            }

            if (type == typeof(TimeSpan)) {
                TimeSpan timeSpan = (TimeSpan)obj;
                long ticks = timeSpan.Ticks;
                long seconds = ticks / TimeSpan.TicksPerSecond;
                int nanoseconds = (int)(ticks % TimeSpan.TicksPerSecond) * Duration.NanosecondsPerTick;

                writer.WriteVarNum((ulong)seconds);
                writer.WriteVarInt((uint)nanoseconds);
                return;
            }

            throw new Exception("No conversion defined for primitive type: " + type.ToString());
        }

        void SerializeArray(Type type, object obj, DataWriter writer) {
            Array arr = (Array)obj;
            writer.WriteVarNum((ulong)arr.Length);

            foreach (object eachObj in arr) {
                Serialize(type, eachObj, writer);
            }
        }

        void SerializeIList(Type type, IList obj, DataWriter writer) {
            writer.WriteVarNum((ulong)obj.Count);

            foreach (object eachObj in obj) {
                Serialize(type, eachObj, writer);
            }
        }

        //TODO: Optimize by checking for other types with length method (IList, ect) before using this
        void SerializeIEnumerable(Type type, IEnumerable enumerable, DataWriter writer) {
            int length = 0;
            
            foreach (object eachObj in enumerable) {
                length += 1;
            }

            writer.WriteVarNum((ulong)length);

            foreach (object eachObj in enumerable) {
                Serialize(type, eachObj, writer);
            }
        }

        void SerializeDictionary(Type K, Type T, IDictionary dict, DataWriter writer) {
            var kvpType = typeof(KeyValuePair<,>);
            Type KeyValueType = kvpType.MakeGenericType(K, T);

            //We force the creation of an entity mapper even though KeyValuePair only has public getters
            GetEntityMapper(KeyValueType, true, false, false, true);
            writer.WriteVarNum((ulong)dict.Count);

            foreach (object eachPair in dict) {
                Serialize(KeyValueType, eachPair, writer);
            }
        }

        void SerializeObject(Type type, object obj, DataWriter writer) {
            var entity = GetEntityMapper(type);

            if (entity.CustomSerializers()) {
                entity.Serializer.Invoke(obj, new object[] { this, writer });
                return;
            }

            foreach (MemberMapper member in entity.Members) {
                object value = member.Getter(obj);
                Serialize(member.DataType, value, writer, member.SkipType, member.SkipIsNull);
            }
        }
    }
}
