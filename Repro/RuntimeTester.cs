namespace CloudRetailer.Pos
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Contracts;
    using Contracts.BusinessRules;

    public class RuntimeTester
    {
        public RuntimeTester(IPosApplication posApplication)
        {
			//ABHISHEK RAKSHIT
            PosApplication = posApplication;
        }

        public IPosApplication PosApplication { get; }

        public IEnumerable<RuntimeTestResult> RunAll()
        {
            var testTypes = PosApplication.PlugInService.QueryPlugInContracts<IRuntimeTest>().Select(i => i.GetType());
            foreach (var testType in testTypes)
            {
                var test = Activator.CreateInstance(testType) as IRuntimeTest;
                yield return Run(test);
            }
        }

        private RuntimeTestResult Run(IRuntimeTest runtimeTest)
        {            
            try
            {
                //TODO: think of a way to streamline registering listeners, right now they are registere in the test, but unregistered in the runtime tester
                return runtimeTest.Run(PosApplication);
            }
            catch (Exception ex)
            {
                return RuntimeTestResult.Error(runtimeTest, ex.Message);
            }
            finally
            {
                PosApplication.MessagingService.UnregisterListener(runtimeTest);
            }
        }
    }
}