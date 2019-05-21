using System;
using System.Net;
using NUnit.Framework;
using ViOpt;

[TestFixture]
class TestRequest
{
    HttpWebRequest _request;
    VideoRecord FakeRecord;
    [SetUp]
    public void Setup()
    {

    }
    [Test]
    [TestCase(2, 2)]
    public void Shoud_return_true(int a, int b)
    {
        Assert.AreEqual(a, b);
    }


}