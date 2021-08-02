using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Core;
using JetBrains.Diagnostics;
using JetBrains.Diagnostics.Internal;
using JetBrains.Lifetimes;
using JetBrains.Threading;
using NUnit.Framework;

namespace Test.Lifetimes.Lifetimes
{


  public class LifetimeTest : LifetimesTestBase
  {

    // ReSharper disable once InconsistentNaming
    LifetimeDefinition def;
    // ReSharper disable once InconsistentNaming
    private Lifetime lt => def.Lifetime;

    [SetUp]
    public void BeforeTest()
    {
      def = new LifetimeDefinition();
      def.Id = TestContext.CurrentContext.Test.Name;
    }

    class FailureException : Exception
    {}
    private void Fail()
    {
      throw new FailureException();
    }
    private T Fail<T>()
    {
      throw new FailureException();
    }

    [Test]
    public void TestEmptyLifetime()
    {
      def.Terminate();
      def.Terminate();
    }

    [Test]
    public void TestActionsSequence()
    {
      var log = new List<int>();

      def.Lifetime.OnTermination(() => log.Add(1));
      def.Lifetime.OnTermination(() => log.Add(2));
      def.Lifetime.OnTermination(() => log.Add(3));

      def.Terminate();

      Assert.AreEqual(new []{3,2,1}, log.ToArray());
    }


    [Test]
    public void TestNestedLifetime()
    {
      var log = new List<int>();

      def.Lifetime.OnTermination(() => log.Add(1));
      new LifetimeDefinition(def.Lifetime).Lifetime.OnTermination(() => log.Add(2));
      def.Lifetime.OnTermination(() => log.Add(3));

      def.Terminate();

      Assert.AreEqual(new []{3,2,1}, log.ToArray());
    }

    [Test]
    public void TestUsing()
    {
      Lifetime lf;
      Lifetime.Using(l =>
      {
        lf = l;
        Assert.True(lf.IsAlive);
        Assert.False(lf.IsEternal);
      });
    }

#if !NET35
    [Test]
    public void TestTerminationWithAsyncTimeout()
    {
      var lin = new Linearization();
      lin.Enable();

      var log = new List<int>();

      var task = Task.Run(() =>
      {
        var first = lt.TryExecute(() =>
        {
          log.Add(0);
          Assert.AreEqual(LifetimeStatus.Alive, lt.Status);
          Assert.True(lt.IsAlive);

          lin.Point(0);
          log.Add(1);

          SpinWaitEx.SpinUntil(() => def.Status == LifetimeStatus.Canceling);
          Assert.False(lt.IsAlive);
        });

        //shouldn't execute
        var second = lt.TryExecute(() => { log.Add(2); });

        Assert.True(first.Succeed);
        Assert.False(second.Succeed);
      });

      def.Lifetime.OnTermination(() => {log.Add(-1);});

      lin.Point(1);
      def.Terminate();
      lin.Point(2);

      task.Wait();
      Assert.AreEqual(new []{0, 1, -1}, log.ToArray());

    }
#endif

    [Test]
    public void TestEternal()
    {
      Assert.True(Lifetime.Eternal.IsEternal);

      //doesn't fail
      Lifetime.Eternal.OnTermination(() => { });
    }

    [Test]
    public void TestEquals()
    {
      Lifetime eternal = default;
      Assert.AreEqual(Lifetime.Eternal, eternal);
      Assert.AreEqual(Lifetime.Eternal, Lifetime.Eternal);
      Assert.AreEqual(eternal, eternal);

      Assert.True(Lifetime.Eternal == eternal);

      Assert.AreNotEqual(Lifetime.Eternal, Lifetime.Terminated);
      Assert.False(Lifetime.Eternal == Lifetime.Terminated);
      Assert.False(eternal == Lifetime.Terminated);
    }

    [Test]
    public void TestTerminated()
    {
      Assert.True(Lifetime.Terminated.Status == LifetimeStatus.Terminated);
    }

    [Test]
    public void StackTrace1()
    {
      def = new LifetimeDefinition();
      lt.TryExecute(() => { def.Terminate(); }).Unwrap();
    }

    [Test]
    public void StackTrace2()
    {
      lt.TryExecute(Fail);
    }

    [Test]
    public void StackTrace3()
    {
      lt.TryExecute(Fail<int>);
    }

    [Test]
    public void TestLongTryExecute()
    {
      const string expectedWarningText = "can't wait for `ExecuteIfAlive` completed on other thread";
      const string expectedExceptionText = "ExecuteIfAlive after termination of";
      bool warningReceived = false, exceptionReceived = false;

      Lifetime.Using(lifetime =>
      {
        void LoggerHandler(LeveledMessage message)
        {
          if (message.Level == LoggingLevel.WARN && message.FormattedMessage.Contains(expectedWarningText))
            warningReceived = true;
        }

        lifetime.Bracket(
          () => TestLogger.ExceptionLogger.Handlers += LoggerHandler,
          () => TestLogger.ExceptionLogger.Handlers -= LoggerHandler
          );

        var lifetimeDefinition = lifetime.CreateNested();
        var lifetimeTerminatedEvent = new ManualResetEvent(false);
        var backgroundThreadIsInTryExecuteEvent = new ManualResetEvent(false);
        var thread = new Thread(() => lifetimeDefinition.Lifetime.TryExecute(() =>
        {
          backgroundThreadIsInTryExecuteEvent.Set();
          lifetimeTerminatedEvent.WaitOne();
        }));
        thread.Start();
        backgroundThreadIsInTryExecuteEvent.WaitOne();
        lifetimeDefinition.Terminate();
        lifetimeTerminatedEvent.Set();
        thread.Join();
        try
        {
          TestLogger.ExceptionLogger.ThrowLoggedExceptions();
        }
        catch (Exception e)
        {
          if (!e.Message.Contains(expectedExceptionText))
            throw;

          exceptionReceived = true;
        }
      });

      Assert.IsTrue(warningReceived, "Warning `{0}` must have been logged", expectedWarningText);
      Assert.IsTrue(exceptionReceived, "Exception `{0}` must have been logged", expectedExceptionText);
    }

    [Test]
    public void TestBracketGood()
    {

      var log = 0;
      void Inner(Action action)
      {
        log = 0;
        def = new LifetimeDefinition();
        action();
        Assert.AreEqual(1, log);
        def.Terminate();
        Assert.AreEqual(11, log);
      }

      //Action, Action
      Inner( () => lt.Bracket(() => log += 1, () => log += 10));

      //Func<T>, Action
      Inner(() => Assert.AreEqual(1, lt.Bracket(() =>
        {
          log += 1;
          return 1;
        },

        () => { log += 10; }
      )));

      //Func<T>, Action<T>
      Inner(() => Assert.AreEqual(10, lt.Bracket(() =>
        {
          log += 1;
          return 10;
        },

        x => { log += x; }
      )));
    }


    [Test]
    public void StackTrace4()
    {
      var log = 0;
      def.Terminate();
      void Inner(Action action)
      {
        log = 0;
        action();
        Assert.AreEqual(0, log);
        def.Terminate(); //once more
        Assert.AreEqual(0, log);
      }

      //Action, Action
      Inner( () => lt.Bracket(() => log += 1, () => log += 10));

      //Func<T>, Action
      Inner(() => Assert.AreEqual(1, lt.Bracket(() =>
        {
          log += 1;
          return 1;
        },

        () => { log += 10; }
      )));

      //Func<T>, Action<T>
      Inner(() => Assert.AreEqual(10, lt.Bracket(() =>
        {
          log += 1;
          return 10;
        },

        x => { log += x; }
      )));
    }


    [Test]
    public void StackTrace5()
    {
      var log = 0;
      void Inner(Action action)
      {
        def = new LifetimeDefinition();
        log = 0;
        action();
        Assert.AreEqual(1, log);
        def.Terminate(); //once more
        Assert.AreEqual(1, log);
      }

      //Action, Action
      Inner(() => lt.Bracket(() =>
      {
        log += 1;
        Fail();
      }, () => log += 10));

      //Func<T>, Action
      Inner(() => Assert.AreEqual(1, lt.Bracket(() =>
        {
          log += 1;
          return Fail<int>();
        },

        () => { log += 10; }
      )));

      //Func<T>, Action<T>
      Inner(() => Assert.AreEqual(10, lt.Bracket(() =>
        {
          log += 1;
          return Fail<int>();
        },

        x => { log += x; }
      )));
    }


    [Test]
    public void TestBracketTerminationInOpening()
    {
      var log = 0;
      void Inner(Action action)
      {
        def = new LifetimeDefinition();
        def.AllowTerminationUnderExecution = true;
        log = 0;
        action();
        Assert.AreEqual(11, log);
        def.Terminate(); //once more
        Assert.AreEqual(11, log);
      }

      //Action, Action
      Inner(() => lt.Bracket(() =>
      {
        log += 1;
        def.Terminate();
      }, () => log += 10));

      //Func<T>, Action
      Inner(() => Assert.AreEqual(1, lt.Bracket(() =>
        {
          log += 1;
          def.Terminate();
          return 1;
        },

        () => { log += 10; }
      )));

      //Func<T>, Action<T>
      Inner(() => Assert.AreEqual(10, lt.Bracket(() =>
        {
          log += 1;
          def.Terminate();
          return 10;
        },

        x => { log += x; }
      )));
    }



    [Test]
    public void TestTryBracketGood()
    {

      var log = 0;
      void Inner(Action action)
      {
        log = 0;
        def = new LifetimeDefinition();
        action();
        Assert.AreEqual(1, log);
        def.Terminate();
        Assert.AreEqual(11, log);
      }


      //Action + Action
      Inner(() => Assert.True(Result.Unit == lt.TryBracket(() =>
        {
          log += 1;
        },

        () => { log += 10; }
      )));
      Inner(() => Assert.True(Result.Unit == lt.TryBracket(() =>
                                {
                                  log += 1;
                                },

                                () => { log += 10; }
                                , true)));


      //Func<T> + Action
      Inner(() => Assert.True(Result.Success(1) == lt.TryBracket(() =>
                                {
                                  log += 1;
                                  return 1;
                                },

                                () => { log += 10; }
                              )));
      Inner(() => Assert.True(Result.Success(1) == lt.TryBracket(() =>
                                {
                                  log += 1;
                                  return 1;
                                },

                                () => { log += 10; }
                                , true)));


      //Func<T> + Action<T>
      Inner(() => Assert.True(Result.Success(10) == lt.TryBracket(() =>
                                {
                                  log += 1;
                                  return 10;
                                },

                                x => { log += x; }
                              )));
      Inner(() => Assert.True(Result.Success(10) == lt.TryBracket(() =>
                                {
                                  log += 1;
                                  return 10;
                                },

                                x => { log += x; }
                                , true)));

    }


    [Test]
    public void TestTryBracketCanceled()
    {

      var log = 0;
      void Inner(Action action)
      {
        log = 0;
        def.Terminate();
        action();
        Assert.AreEqual(0, log);
        def.Terminate();
        Assert.AreEqual(0, log);
      }

      //Action + Action
      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1; },

          () => { log += 10; }
        );
        Assert.True(res.Canceled);
        Assert.True(res.Exception is LifetimeCanceledException lce && lce.Lifetime == lt);
      });

      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1; },

          () => { log += 10; },
          true
        );
        Assert.True(res.Canceled);
        Assert.True(res.Exception is LifetimeCanceledException lce && lce.Lifetime == lt);
      });


      //Func<T> + Action
      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1;
            return 1;
          },

          () => { log += 10; }
        );
        Assert.True(res.Canceled);
        Assert.True(res.Exception is LifetimeCanceledException lce && lce.Lifetime == lt);
      });

      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1; return 1;},

          () => { log += 10; },
          true
        );
        Assert.True(res.Canceled);
        Assert.True(res.Exception is LifetimeCanceledException lce && lce.Lifetime == lt);
      });

      //Func<T> + Action<T>
      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1;
            return 1;
          },

          x => { log += x; }
        );
        Assert.True(res.Canceled);
        Assert.True(res.Exception is LifetimeCanceledException lce && lce.Lifetime == lt);
      });

      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1; return 1;},

          x => { log += x; },
          true
        );
        Assert.True(res.Canceled);
        Assert.True(res.Exception is LifetimeCanceledException lce && lce.Lifetime == lt);
      });

    }


    [Test]
    public void StackTrace6()
    {
      var log = 0;
      void Inner(Action action)
      {
        log = 0;
        def = new LifetimeDefinition();
        action();
        Assert.AreEqual(1, log);
        def.Terminate();
        Assert.AreEqual(1, log);
      }

      //Action + Action
      Inner(() =>
      {
        lt.TryBracket(() => { log += 1; Fail(); },
          () => { log += 10;  }
        );
      });

      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1; Fail(); },
          () => { log += 10; },
          true
        );
        Assert.True(res.FailedNotCanceled);
        Assert.True(res.Exception is FailureException);
      });


      //Func<T> + Action
      Inner(() =>
      {
        Assert.Throws<FailureException>( () => lt.TryBracket(() => { log += 1; return Fail<int>(); },
          () => { log += 10;  }
        ));
      });

      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1; return Fail<int>(); },
          () => { log += 10; },
          true
        );
        Assert.True(res.FailedNotCanceled);
        Assert.True(res.Exception is FailureException);
      });


      //Func<T> + Action<T>
      Inner(() =>
      {
        Assert.Throws<FailureException>(() => lt.TryBracket(() => { log += 1;
            return Fail<int>();
          },

          x => { log += x; }
        ));
      });

      Inner(() =>
      {
        var res = lt.TryBracket(() => { log += 1; return Fail<int>(); },

          x => { log += x; },
          true
        );
        Assert.True(res.FailedNotCanceled);
        Assert.True(res.Exception is FailureException);
      });
    }



    [Test]
    public void StackTrace7()
    {

      var log = 0;


      void InnerSuccess<T>(Func<bool, Result<T>> action)
      {
        log = 0;
        def = new LifetimeDefinition {AllowTerminationUnderExecution = true};

        Assert.True(action(false).Succeed);
        Assert.AreEqual(11, log);

        log = 0;
        def = new LifetimeDefinition{AllowTerminationUnderExecution = true};
        Assert.True(action(true).Succeed);
        Assert.AreEqual(11, log);

        Assert.AreEqual(11, log);
      }

      void InnerFail<T>(Func<bool, Result<T>> action)
      {
        log = 0;
        def = new LifetimeDefinition{AllowTerminationUnderExecution = true};

        action(false);
      }

      //Action + Action
      InnerSuccess(wrap => lt.TryBracket(() =>
        {
          log += 1;
          def.Terminate();
        },
        () => { log += 10; },
        wrap
      ));

      InnerFail(wrap => lt.TryBracket(() =>
        {
          log += 1;
          def.Terminate();
        },
        () => { log += 10; Fail(); },
        wrap
      ));


      //Func<T> + Action
      InnerSuccess(wrap => lt.TryBracket(() =>
        {
          log += 1;
          def.Terminate();
          return 1;
        },
        () => { log += 10; },
        wrap
      ));


      // Func<T> + Action<T>
      InnerFail(wrap => lt.TryBracket(() =>
        {
          log += 1;
          def.Terminate();
          return 10;
        },
        x => { log += x; Fail();},
        wrap
      ));
    }


    [Test]
    public void TestAddTerminationActionToTerminatedLifetime()
    {
      int executed = 0;
      def.Terminate();

      //actions
      Assert.False(lt.TryOnTermination(() => { executed++; }));
      Assert.AreEqual(0, executed);  //no change

      Assert.Throws<InvalidOperationException>(() => lt.OnTermination(() => { executed++; }));
      Assert.AreEqual(1, executed);

      //dispose
      Assert.False(lt.TryOnTermination(() => { executed++; }));
      Assert.AreEqual(1, executed); //no change

      Assert.Throws<InvalidOperationException>(() => lt.OnTermination(() => { executed++; }));
      Assert.AreEqual(2, executed);

    }

#if !NET35
    [Test]
    public void TestTaskAttachment()
    {
      int executed = 0;
      lt.ExecuteAsync(async () =>
      {
        await Task.Yield();
        executed += 1;
      });
      lt.OnTermination(() => executed *= 2);

      def.Terminate(); //will wait for task
      Assert.AreEqual(2, executed);
    }


    [Test]
    public void TestTaskWithTerminatedLifetime() {
      var task = Lifetime.Terminated.TryExecuteAsync(async () =>
      {
        await Task.Yield();
        return 0;
      });

      Assert.True(task.IsCanceled);
    }

    [Test]
    public void ConcurrentStackTrace8() {
      var task = lt.ExecuteAsync(async () =>
      {
        await Task.Yield();
        throw new Exception();
      });

      task.Wait();
    }
#endif

    [Test]
    public void TestAllInnerLifetimesTerminatedExceptLast()
    {
      var o = lt.CreateNested();
      for (int i = 0; i < 100; i++)
      {
        var n = lt.CreateNested();
        o.Terminate();
        o = n;
      }

      var resCount = def.GetDynamicField("myResCount");
      Assert.AreEqual(2, resCount); //one is dead

      var resourcesCapacity = def.GetDynamicField("myResources").GetDynamicProperty("Length");
      Assert.AreEqual(2, resourcesCapacity); //one is dead
    }


//    [Test]
//    public void TestScopeLifetime()
//    {
//      Lifetime lf;
//      using (var scoped = new ScopedLifetime())
//      {
//        lf = scoped;
//        lf.AssertIsAlive();
//      }
//       Assert.False(lf.IsAlive);
//    }
//
//    [Test]
//    public void TestScopedLifetimeWithAliveParent()
//    {
//      Lifetime lf;
//      using (var scoped = new ScopedLifetime(lt))
//      {
//        lf = scoped;
//        lf.AssertIsAlive();
//      }
//      Assert.True(lt.IsAlive);
//      Assert.False(lf.IsAlive);
//    }
//
//    [Test]
//    public void TestScopedLifetimeWithTerminatingParent()
//    {
//      Lifetime lf;
//      using (var scoped = new ScopedLifetime(lt))
//      {
//        lf = scoped;
//        lf.AssertIsAlive();
//        def.Terminate();
//        Assert.False(lf.IsAlive);
//      }
//      Assert.False(lt.IsAlive);
//      Assert.False(lf.IsAlive);
//    }
//
//    [Test]
//    public void TestScopedLifetimeWithTerminatedParent()
//    {
//      Lifetime lf;
//      using (var scoped = new ScopedLifetime(lt))
//      {
//        lf = scoped.Instance;
//        lf.AssertIsAlive();
//        def.Terminate();
//        Assert.False(lf.IsAlive);
//      }
//      Assert.False(lt.IsAlive);
//      Assert.False(lf.IsAlive);
//    }

#if !NET35
    [Test]
    public void TestCancellationToken1()
    {
      def.Terminate();
      var task = Task.Run(() => {}, lt);
      Log.Root.CatchAndDrop(task.Wait);
      Assert.AreEqual(TaskStatus.Canceled, task.Status);
    }


    [Test]
    public void TestCancellationToken2()
    {
      var evt = new ManualResetEvent(false);
      var task = Task.Run(() => { evt.Set();}, lt);

      evt.WaitOne();
      def.Terminate();

      Log.Root.CatchAndDrop(task.Wait);

      Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
    }

    [Test]
    public void TestCancellationToken3()
    {
      var task = Task.Run(() =>
      {
        def.Terminate();
        lt.OnTermination(() => { });
      }, lt);

      Log.Root.CatchAndDrop(task.Wait);
      Assert.AreEqual(TaskStatus.Faulted, task.Status);
    }

    [Test]
    public void TestCancellationToken4()
    {
      var task = Task.Run(() =>
      {
        def.Terminate();
        def.ThrowIfNotAlive();
      }, lt);

      Log.Root.CatchAndDrop(task.Wait);
      Assert.AreEqual(TaskStatus.Faulted, task.Status);
    }

    [Test, Ignore("Fails on build server")]
    public void TestTooLongExecuting()
    {

      var oldTimeout = LifetimeDefinition.WaitForExecutingInTerminationTimeoutMs;
      LifetimeDefinition.WaitForExecutingInTerminationTimeoutMs = 100;

      try
      {

        AutoResetEvent sync = new AutoResetEvent(false);
        var task = lt.StartAttached(TaskScheduler.Default, () => sync.WaitOne());
        def.Terminate();
        var ex = Assert.Catch(ThrowLoggedExceptions, $"First exception from {nameof(LifetimeDefinition)}.Terminate"); //first from terminate
        Assert.True(ex.Message.Contains("ExecuteIfAlive"));

        sync.Set();


        task.Wait();
        ex = Assert.Catch(ThrowLoggedExceptions, $"Second exception from {nameof(LifetimeDefinition.ExecuteIfAliveCookie)}.Dispose"); //second from terminate
        Assert.True(ex.Message.Contains("ExecuteIfAlive"));

      }
      finally
      {
        LifetimeDefinition.WaitForExecutingInTerminationTimeoutMs = oldTimeout;
      }

    }
#endif



    [Test]
    public void T000_Items()
    {
      int count = 0;
      Lifetime.Using(lifetime =>
      {
        lifetime.OnTermination(() => count++);
        lifetime.AddDispose(Disposable.CreateAction(() => count++));
        lifetime.OnTermination(() => count++);
        lifetime.OnTermination(Disposable.CreateAction(() => count++));
      });

      Assert.AreEqual(4, count, "Mismatch.");
    }

    [Test]
    public void T010_SimpleOrder()
    {
      var entries = new List<int>();
      int x= 0 ;
      Lifetime.Using(lifetime =>
      {
        int a = x++;
        lifetime.OnTermination(() => entries.Add(a));
        int b = x++;
        lifetime.AddDispose(Disposable.CreateAction(() => entries.Add(b)));
        int c = x++;
        lifetime.OnTermination(Disposable.CreateAction(() => entries.Add(c)));
        int d = x++;
        lifetime.AddDispose(Disposable.CreateAction(() => entries.Add(d)));
      });

      CollectionAssert.AreEqual(Enumerable.Range(0, entries.Count).Reverse().ToArray(), entries, "Order FAIL.");
    }

    [Test]
    public void T020_DefineNestedOrder()
    {
      var entries = new List<int>();
      int x= 0 ;

      Func<Action> FMakeAdder = () => { var a = x++; return () => entries.Add(a); };  // Fixes the X value at the moment of FMakeAdder call.

      bool flag = false;

      Lifetime.Using(lifetime =>
      {
        lifetime.OnTermination(FMakeAdder());
        lifetime.AddDispose(Disposable.CreateAction(FMakeAdder()));
        Lifetime.Define(lifetime, atomicAction:(lifeNested) => { lifeNested.OnTermination(FMakeAdder()); lifeNested.OnTermination(FMakeAdder()); lifeNested.OnTermination(FMakeAdder());});
        lifetime.AddDispose(Disposable.CreateAction(FMakeAdder()));
        Lifetime.Define(lifetime, atomicAction:(lifeNested) => { lifeNested.OnTermination(FMakeAdder()); lifeNested.OnTermination(FMakeAdder()); lifeNested.OnTermination(FMakeAdder());});
        lifetime.AddDispose(Disposable.CreateAction(FMakeAdder()));
        Lifetime.Define(lifetime, atomicAction:(lifeNested) => lifeNested.OnTermination(() => flag = true)).Terminate();
        Assert.IsTrue(flag, "Nested closing FAIL.");
        flag = false;
        lifetime.AddDispose(Disposable.CreateAction(FMakeAdder()));
      });

      Assert.IsFalse(flag, "Nested closed twice.");

      CollectionAssert.AreEqual(System.Linq.Enumerable.Range(0, entries.Count).Reverse().ToArray(), entries, "Order FAIL.");

    }

#if !NET35
    [Test]
    public void CancellationTokenTest()
    {
      var def = Lifetime.Define();

      var sw = new SpinWait();
      var task = Task.Run(() =>
      {
        while (true)
        {
          def.Lifetime.ThrowIfNotAlive();
          sw.SpinOnce();
        }
      }, def.Lifetime);

      Thread.Sleep(100);
      def.Terminate();

      try
      {
        task.Wait();
      }
      catch (AggregateException e)
      {
        Assert.True(task.IsCanceled);
        Assert.True(e.IsOperationCanceled());
        return;
      }

      Assert.Fail("Unreachable");
    }


    [Test]
    public void ConcurrentStackTrace9()
    {
      var def = Lifetime.Define();
      def.Terminate();

      var task = Task.Run(() =>
      {
        Assertion.Fail("Unreachable");
      }, def.Lifetime);

      task.Wait();

      Assert.True(task.IsCanceled);
    }

    [Test]
    public void TestCancellationEternalLifetime()
    {
      var lt = Lifetime.Eternal;

      var task = Task.Run(() =>
      {
        lt.ThrowIfNotAlive();
        Thread.Yield();
      }, lt);

      task.Wait();

      Assert.True(task.Status == TaskStatus.RanToCompletion);
    }

    [Test]
    public void TestCreateTaskCompletionSource()
    {
      Assert.True(Lifetime.Terminated.CreateTaskCompletionSource<Unit>().Task.IsCanceled);



      var t = lt.CreateTaskCompletionSource<Unit>().Task;
      Assert.False(t.IsCompleted);

      def.Terminate();
      Assert.True(t.IsCanceled);
    }



    [Test]
    public void TestSynchronizeTaskCompletionSource()
    {
      //lifetime terminated
      var tcs = new TaskCompletionSource<Unit>();
      Lifetime.Terminated.CreateNested().SynchronizeWith(tcs);
      Assert.True(tcs.Task.IsCanceled);


      //tcs completed
      tcs = new TaskCompletionSource<Unit>();
      tcs.SetResult(Unit.Instance);

      Lifetime.Terminated.CreateNested().SynchronizeWith(tcs); //nothing
      Lifetime.Eternal.CreateNested().SynchronizeWith(tcs); //nothing

      def.SynchronizeWith(tcs);
      Assert.True(lt.Status == LifetimeStatus.Terminated);


      //lifetime terminates first
      tcs = new TaskCompletionSource<Unit>();
      var d = new LifetimeDefinition();
      d.SynchronizeWith(tcs);
      Assert.True(d.Lifetime.IsAlive);
      Assert.False(tcs.Task.IsCompleted);

      d.Terminate();
      Assert.True(tcs.Task.IsCanceled);

      //tcs terminates first
      tcs = new TaskCompletionSource<Unit>();
      d = new LifetimeDefinition();
      d.SynchronizeWith(tcs);

      tcs.SetCanceled();
      Assert.True(d.Lifetime.Status == LifetimeStatus.Terminated);
    }

    [Test]
    public void TestTerminatesAfter()
    {
      var lf = TestLifetime.CreateTerminatedAfter(TimeSpan.FromMilliseconds(100));
      Assert.True(lf.IsAlive);
      Thread.Sleep(200);
      Assert.True(lf.IsNotAlive);

      lf = TestLifetime.CreateTerminatedAfter(TimeSpan.FromMilliseconds(100));
      Assert.True(lf.IsAlive);
      LifetimeDefinition.Terminate();
      Assert.True(lf.IsNotAlive);

      Thread.Sleep(200);
      Assert.True(lf.IsNotAlive);
    }

#endif

    [Test]
    public void SimpleOnTerminationStressTest()
    {
      for (int i = 0; i < 100; i++)
      {
        using var lifetimeDefinition = new LifetimeDefinition();
        var lifetime = lifetimeDefinition.Lifetime;
        int count = 0;
        const int threadsCount = 10;
        const int iterations = 1000;
        Task.Factory.StartNew(() =>
        {
          for (int j = 0; j < threadsCount; j++)
          {
            Task.Factory.StartNew(() =>
            {
              for (int k = 0; k < iterations; k++)
                lifetime.OnTermination(() => count++);
            }, TaskCreationOptions.AttachedToParent | TaskCreationOptions.LongRunning);
          }
        }).Wait();

        lifetimeDefinition.Terminate();
        Assert.AreEqual(threadsCount * iterations, count);
      }
    }
  }
}
