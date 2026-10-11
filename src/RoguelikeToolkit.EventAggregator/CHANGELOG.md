# Changelog

## [0.4.0](https://github.com/myarichuk/RoguelikeToolkit.Shared/compare/event-aggregator-v0.3.0...event-aggregator-v0.4.0) (2026-10-11)


### ⚠ BREAKING CHANGES

* **event-aggregator:** the EventAggregator class is now EventBus (it collided with the RoguelikeToolkit.EventAggregator namespace, so consumers under any RoguelikeToolkit.* namespace got CS0118). IEventAggregator is unchanged. EventChannel<T> and SubscriberManager are now internal.

### Features

* **event-aggregator:** rename EventAggregator to EventBus, allocation-free publish ([257154a](https://github.com/myarichuk/RoguelikeToolkit.Shared/commit/257154af1bd18a792b79622f49959c8575205c34))

## [0.3.0](https://github.com/myarichuk/RoguelikeToolkit.Shared/compare/event-aggregator-v0.2.0...event-aggregator-v0.3.0) (2026-10-10)


### Features

* **event-aggregator:** expose Unsubscribe and SubscriberCount on the aggregator facade ([3a62ef1](https://github.com/myarichuk/RoguelikeToolkit.Shared/commit/3a62ef17e062f6ac18b9dc13db12fb88cc22ccdb))

## [0.2.0](https://github.com/myarichuk/RoguelikeToolkit.Shared/compare/event-aggregator-v0.1.0...event-aggregator-v0.2.0) (2026-10-10)


### Features

* **event-aggregator:** expose Unsubscribe and SubscriberCount on the aggregator facade ([3a62ef1](https://github.com/myarichuk/RoguelikeToolkit.Shared/commit/3a62ef17e062f6ac18b9dc13db12fb88cc22ccdb))
