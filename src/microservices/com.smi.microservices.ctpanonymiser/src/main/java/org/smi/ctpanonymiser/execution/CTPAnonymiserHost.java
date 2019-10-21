package org.smi.ctpanonymiser.execution;

import org.apache.commons.cli.CommandLine;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.smi.common.execution.IMicroserviceHost;
import org.smi.common.messaging.IProducerModel;
import org.smi.common.options.GlobalOptions;
import org.smi.common.rabbitMq.RabbitMqAdapter;
import org.smi.ctpanonymiser.messaging.CTPAnonymiserConsumer;
import org.smi.ctpanonymiser.util.DicomAnonymizerToolBuilder;

import java.io.File;
import java.io.FileNotFoundException;
import java.nio.file.Paths;

public class CTPAnonymiserHost implements Runnable, IMicroserviceHost {

	private static final Logger _logger = LoggerFactory.getLogger(CTPAnonymiserHost.class);

	private final RabbitMqAdapter _rabbitMqAdapter;
	private final CTPAnonymiserConsumer _consumer;
	private IProducerModel _producer;

	private final GlobalOptions _options;

	public CTPAnonymiserHost(GlobalOptions options, CommandLine cliOptions) throws FileNotFoundException {

		_options = options;

		_logger.trace("Setting up CTPAnonymiserHost");

		File anonScriptFile = new File(Paths.get(cliOptions.getOptionValue("a")).toString());
		_logger.debug("anonScriptFile: " + anonScriptFile.getPath());

		if (!anonScriptFile.exists() || anonScriptFile.isDirectory())
			throw new IllegalArgumentException("Cannot find anonymisation script file: " + anonScriptFile.getPath());

		String fsRoot = options.FileSystemOptions.getFileSystemRoot();
		if (!CheckValidDirectory(fsRoot)) {
			throw new FileNotFoundException("Given filesystem root is not valid: " + fsRoot);
		}

		String exRoot = options.FileSystemOptions.getExtractRoot();
		if (!CheckValidDirectory(exRoot)) {
			throw new FileNotFoundException("Given extraction root is not valid: " + exRoot);
		}

		_rabbitMqAdapter = new RabbitMqAdapter(options.RabbitOptions, "CTPAnonymiserHost");
		_logger.debug("Connected to RabbitMQ server version " + _rabbitMqAdapter.getRabbitMqServerVersion());

		try {

			_producer = _rabbitMqAdapter.SetupProducer(options.CTPAnonymiserOptions.ExtractFileStatusProducerOptions);

		} catch (IllegalArgumentException e) {

			e.printStackTrace();
		}

		// Build the SMI Anonymiser tool
		SmiCtpProcessor anonTool = new DicomAnonymizerToolBuilder().tagAnonScriptFile(anonScriptFile).check(null).buildDat();

		final String routingKey = "";

		_consumer = new CTPAnonymiserConsumer(
				_producer,
				anonTool,
				routingKey,
				fsRoot,
				exRoot);

		_logger.info("CTPAnonymiserHost created successfully");
	}

	public IProducerModel getProducer() {
		return _producer;
	}

	/**
	 * Entry point for the thread (Runnable interface)
	 */
	@Override
	public void run() {

		// Start the consumer
		_rabbitMqAdapter.StartConsumer(_options.CTPAnonymiserOptions.ExtractFileConsumerOptions, _consumer);

		try {

			Thread.currentThread().join();

		} catch (InterruptedException e) {

			e.printStackTrace();
		}

		_logger.info("Anonymiser host finishing");
	}

	public void Shutdown() {

		_logger.info("Host shutdown called");

		_rabbitMqAdapter.Shutdown();
	}

	private boolean CheckValidDirectory(String path) {

		File f = new File(Paths.get(path).toString());

		return (f.exists() && f.isDirectory() && f.canRead());
	}
}