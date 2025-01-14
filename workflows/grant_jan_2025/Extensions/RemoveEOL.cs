using Bonsai;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

[Combinator]
[Description("Removes \n \r from strings")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class RemoveEOL
{
    public IObservable<string> Process(IObservable<string> source)
    {
        return source.Select(value => 
        {
            string result = value.Replace("\n",String.Empty);
            result = value.Replace("\r",String.Empty);
            return result;
        });
    }
}
