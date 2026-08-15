using BlogGenerator.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests;

public class SmokeTests
{
    [Test]
    public void Article型を参照できる()
    {
        Assert.That(typeof(Article).Name, Is.EqualTo("Article"));
    }
}
