using CSharp_Library.Utility;
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
        /// Serializes obj to byte[]. Appends type to beginning of data.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns>Serialized Data</returns>
        public byte[] FromObject(object obj) {
            if (obj == null)
                throw new ArgumentNullException("obj");

            DataWriter writer = GetDataWriter();
            writer.Reset();
            Type type = obj.GetType();
            TypeCache typeCache = GetTypeCache(type);
            WriteType(type, obj, writer, typeCache.IsGeneric, true);
            Serialize(type, obj, writer, typeCache, true, true);
            return _writer.CopyBytes();
        }

        /// <summary>
        /// Serializes obj to byte[]. Does not append type to beginning of data.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="obj"></param>
        /// <returns></returns>
        public byte[] FromObject(Type type, object obj) {
            if (obj == null)
                throw new ArgumentNullException("obj");
            if (type == null)
                throw new ArgumentNullException("type");

            DataWriter writer = GetDataWriter();
            writer.Reset();
            Serialize(obj, writer);
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

        public void Serialize(object obj, DataWriter writer, bool skipMetaData = false, bool skipNullByte = false) {
            Type type = (obj != null) ? obj.GetType() : null;
            TypeCache typeCache = GetTypeCache(type);
            Serialize(type, obj, writer, typeCache, skipMetaData, skipNullByte);
        }

        public void Serialize(Type type, object obj, DataWriter writer, TypeCache typeCache, bool skipMetaData = false, bool skipNullByte = false) {
            
            if (obj == null && skipNullByte) {
                throw new Exception("skipNullByte option is set AND object to be written is null. No way to deserialize since deserializer will assume non-null value.");
            }

            if (typeCache.HasChildTypes && skipMetaData == false) {
                WriteType(type, obj, writer, typeCache.IsGeneric);
            } else if (skipNullByte == false && typeCache.CanBeNull()) {
                writer.WriteBoolean((obj != null));
            }

            if (obj == null)
                return;

            if (typeCache.IsNullable) {
                Type[] types = Reflection.GetGenericArgumentsExt(type);
                TypeCache genericCache = GetTypeCache(types[0]);
                Serialize(types[0], obj, writer, genericCache);
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
                if (type == typeof(object))
                    SerializeArray(type, obj, writer);

                else if (type.IsArray)
                    SerializeArray(type.GetElementType(), obj, writer);

                else
                    SerializeList(type, obj, writer);
                return;
            }

            if (typeCache.IsGeneric) {
                if (obj is IDictionary) {
                    Type[] genericArguments = Reflection.GetGenericArgumentsExt(type);
                    Type k = genericArguments[0];
                    Type t = genericArguments[1];
                    SerializeDictionary(k, t, (IDictionary)obj, writer);
                    return;
                }
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
                Serialize(eachObj, writer);
            }
        }

        //TODO: Optimize by checking for other types with length method (IList, ect) before using this
        void SerializeList(Type type, object obj, DataWriter writer) {
            var enumerable = (IEnumerable)obj;
            int length = 0;
            
            foreach (object eachObj in enumerable) {
                length += 1;
            }

            writer.WriteVarNum((ulong)length);

            foreach (object eachObj in enumerable) {
                Serialize(eachObj, writer);
            }
        }

        void SerializeDictionary(Type K, Type T, IDictionary dict, DataWriter writer) {
            var kvpType = typeof(KeyValuePair<,>);
            Type KeyValueType = kvpType.MakeGenericType(K, T);

            //We force the creation of an entity mapper even though KeyValuePair only has public getters
            GetEntityMapper(KeyValueType, true, false, false, true);
            SerializeList(KeyValueType, dict, writer);
        }

        void SerializeObject(Type type, object obj, DataWriter writer) {
            var entity = GetEntityMapper(type);

            if (entity.CustomSerializers()) {
                entity.Serializer.Invoke(obj, new object[] { this, writer });
                return;
            }

            foreach (MemberMapper member in entity.Members) {
                object value = member.Getter(obj);
                Serialize(member.DataType, value, writer, member.Cache, member.SkipType, member.SkipIsNull);
            }
        }
    }
}
