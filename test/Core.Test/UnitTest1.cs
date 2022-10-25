namespace Core.Test
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            Assert.Throws<DivideByZeroException>(() => Class1.Throw());
        }
    }
}
