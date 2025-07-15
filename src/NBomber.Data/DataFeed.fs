namespace NBomber.Data

open NBomber.Contracts

/// <summary>
/// Represents a data source for feeding test data into a load test scenario.
/// </summary>
/// <typeparam name="T">
/// The type of data items provided by the feed.
/// </typeparam>
type IDataFeed<'T> =    
    /// Gets the full collection of data items available in the feed.    
    abstract Items: 'T[]
    
    /// <summary>
    /// Retrieves the next data item from the feed based on the provided scenario context.
    /// </summary>
    /// <param name="scenarioInfo">
    /// Contains contextual information about the executing scenario, which may influence data selection logic.
    /// </param>
    /// <returns>
    /// A single data item of type <typeparamref name="T"/> to be used in the scenario.
    /// </returns>
    abstract GetNextItem: scenarioInfo:ScenarioInfo -> 'T

namespace NBomber.Data.FSharp

    open System
    open NBomber.Data

    /// DataFeed helps inject test data into your load test. It represents a data source.
    module DataFeed =
        
        /// Creates DataFeed that picks constant value per Scenario copy.
        /// Every Scenario copy will have unique constant value.
        let constant data =
            let _items = data |> Seq.toArray

            { new IDataFeed<'T> with
                member _.Items = _items
                member _.GetNextItem(scenarioInfo) =
                    let index = scenarioInfo.InstanceNumber % _items.Length
                    _items[index]
            }     
        
        /// Creates DataFeed that goes back to the top of the sequence once the end is reached.
        let circular data =
            let _items = data |> Seq.toArray                
            let _lockObj = obj()

            let createInfiniteStream (items: 'T seq) = seq {
                while true do
                    yield! items
            }
            
            let infiniteItems = _items |> createInfiniteStream
            let _enumerator = infiniteItems.GetEnumerator()        

            { new IDataFeed<'T> with
                member _.Items = _items
                member _.GetNextItem(scenarioInfo) =
                    lock _lockObj (fun _ ->
                        _enumerator.MoveNext() |> ignore
                        _enumerator.Current
                    )
            }  
        
        /// Creates DataFeed that randomly picks an item per GetNextItem() invocation.
        let random data =
            let _random = Random()
            let _items = data |> Seq.toArray 

            let getRandomItem () =
                let index = _random.Next _items.Length
                _items[index]

            { new IDataFeed<'T> with                
                member _.Items = _items
                member _.GetNextItem(scenarioInfo) = getRandomItem()
            }

namespace NBomber.Data.CSharp

/// DataFeed helps inject test data into your load test. It represents a data source.
type DataFeed =
    
    /// Creates DataFeed that picks constant value per Scenario copy.
    /// Every Scenario copy will have unique constant value.
    static member Constant (data: 'T seq) = NBomber.Data.FSharp.DataFeed.constant data
            
    /// Creates DataFeed that goes back to the top of the sequence once the end is reached.            
    static member Circular (data: 'T seq) = NBomber.Data.FSharp.DataFeed.circular data        
        
    /// Creates DataFeed that randomly picks an item per GetNextItem() invocation.        
    static member Random (data: 'T seq) = NBomber.Data.FSharp.DataFeed.random data