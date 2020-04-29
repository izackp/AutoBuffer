using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using AutoBuffer;

namespace AutoBuffer.Test {

    [AutoBufferType(1)]
    public class Parent {
        [Index(0)]
        public string Hello = "World";

        public Parent() { }
    }

    [AutoBufferType(2)]
    public class Child : Parent {

        [Index(0)]
        public object ParentContainer;

        public Child() : base() {
            Hello = "Nope";
        }
    }

    [AutoBufferType(3)]
    public class ChildList : List<Parent> {

        [Index(0)]
        public Child ch = new Child();

        [Index(1)]
        public object chObj = new Child();

        public ChildList() : base() { }

    }

    public class Test {

        static public void Run() {
            var serializer = new Serializer(Assembly.GetExecutingAssembly());

            var confusingObj = new List<object>();
            var childList = new ChildList();
            childList.Add(new Parent());
            var otherList = new ChildList();
            confusingObj.AddRange(new object[] { childList, new Parent(), new Child(), otherList });

            var data = serializer.FromObject(confusingObj);

            var result = serializer.ToObject<List<object>>(data);
        }
    }
}
