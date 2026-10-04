using BlogGenerator.Core.Images;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
public class ImageFileNameMatcherTests
{
    [TestCase("My Photo.jpg", "prefix My Photo.jpg suffix")]
    [TestCase("a+b[1].jpg", "\"a+b[1].jpg\"")]
    [TestCase("日本語.png", "/img/日本語.png?size=1#part")]
    [TestCase("😀.jpg", "😀.jpg")]
    [TestCase("photo.jpg", "PHOTO.JPG")]
    [TestCase("𐐀.jpg", "𐐨.jpg")]
    public void 書式に依存せずUnicodeと記号と大文字小文字を扱う(string name, string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        new ImageFileNameMatcher([name]).AddMatches(text, result);
        Assert.That(result.SetEquals([name]), Is.True);
    }

    [Test]
    public void 重なりと隣接があっても全ての候補を拾う()
    {
        string[] names = ["photo.jpg", "x-photo.jpg", "oto.jpg"];
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matcher = new ImageFileNameMatcher(names);
        matcher.AddMatches("x-photo.jpgphoto.jpg", result);
        Assert.That(result.SetEquals(names), Is.True);
        matcher.AddMatches("photo.jpg", result);
        Assert.That(result.Count, Is.EqualTo(3));
    }

    [Test]
    public void 空の候補と空の本文を扱える()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        new ImageFileNameMatcher([]).AddMatches("photo.jpg", result);
        new ImageFileNameMatcher(["photo.jpg"]).AddMatches("", result);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ランダムな文字列と候補でもContainsの判定と一致する()
    {
        var random = new Random(20261004);
        string[] atoms = ["a", "B", "i", "İ", "ı", "ſ", "Σ", "σ", "ς", "𐐀", "𐐨", "日", "😀", ".", " ", "+", "[", "]", "/", "_", "-"];
        string Word() => string.Concat(Enumerable.Range(0, random.Next(1, 9)).Select(_ => atoms[random.Next(atoms.Length)]));
        for (var trial = 0; trial < 100; trial++)
        {
            var names = Enumerable.Range(0, 30).Select(_ => Word() + ".jpg").ToArray();
            var text = string.Concat(Enumerable.Range(0, 100).Select(_ => random.Next(3) == 0 ? names[random.Next(names.Length)] : Word()));
            var expected = names.Where(n => text.Contains(n, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            new ImageFileNameMatcher(names).AddMatches(text, actual);
            Assert.That(actual.SetEquals(expected), Is.True, $"trial {trial}");
        }
    }
}
