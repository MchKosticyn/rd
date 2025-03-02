using System;
using System.Threading.Tasks;
using JetBrains.Lifetimes;
using JetBrains.Threading;
using NUnit.Framework;

namespace Test.Lifetimes.Threading
{
#if !NET35
  public class ChannelTest : LifetimesTestBase
  {
    private AsyncChannel<int> myChannel;


    // [Test] TODO: #ignore
    public void SendBlockingStackTrace19()
    {
      var def = new LifetimeDefinition();
      myChannel = new AsyncChannel<int>(def.Lifetime);

      Assert.True(myChannel.IsEmpty);

      myChannel.SendBlocking(1);
      myChannel.SendBlocking(2);
      myChannel.SendBlocking(3);
      Assert.AreEqual(1, myChannel.ReceiveBlocking());
      Assert.AreEqual(2, myChannel.ReceiveBlocking());
      Assert.AreEqual(3, myChannel.ReceiveBlocking());

      def.Terminate();
      myChannel.SendBlocking(0);
    }

    // [Test] TODO: #ignore
    public void RecieveBlockingStackTrace20()
    {
      var def = new LifetimeDefinition();
      myChannel = new AsyncChannel<int>(def.Lifetime);

      Assert.True(myChannel.IsEmpty);

      myChannel.SendBlocking(1);
      myChannel.SendBlocking(2);
      myChannel.SendBlocking(3);
      Assert.AreEqual(1, myChannel.ReceiveBlocking());
      Assert.AreEqual(2, myChannel.ReceiveBlocking());
      Assert.AreEqual(3, myChannel.ReceiveBlocking());

      def.Terminate();
      myChannel.ReceiveBlocking();
    }

    [Test]
    public void TestInfiniteChannelAsync1Thread()
    {
      var def = new LifetimeDefinition();
      myChannel = new AsyncChannel<int>(def.Lifetime);

      Assert.True(myChannel.IsEmpty);

      Assert.True(myChannel.SendAsync(1).Status == TaskStatus.RanToCompletion);
      Assert.True(myChannel.SendAsync(2).Status == TaskStatus.RanToCompletion);
      Assert.AreEqual(1, myChannel.ReceiveAsync().With(t => Assert.AreEqual(TaskStatus.RanToCompletion, t.Status)).Result);
      Assert.AreEqual(2, myChannel.ReceiveAsync().With(t => Assert.AreEqual(TaskStatus.RanToCompletion, t.Status)).Result);

      def.Terminate();
      Assert.True(myChannel.SendAsync(0).IsCanceled);
      Assert.True(myChannel.ReceiveAsync().IsCanceled);
    }

  }
#endif
}
