
package org.smi.common.messaging;

import java.io.IOException;
import java.io.UnsupportedEncodingException;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;

import org.smi.common.messageSerialization.JsonDeserializerWithOptions;
import org.smi.common.messages.MessageHeader;
import org.smi.common.options.ConsumerOptions;
import org.smi.common.rabbitMq.RabbitMqAdapter;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.google.gson.JsonSyntaxException;
import com.rabbitmq.client.AMQP;
import com.rabbitmq.client.AMQP.BasicProperties;
import com.rabbitmq.client.Channel;
import com.rabbitmq.client.Consumer;
import com.rabbitmq.client.DefaultConsumer;
import com.rabbitmq.client.Envelope;
import com.rabbitmq.client.ShutdownSignalException;

/**
 * Class providing default methods for {@link Consumer} interface
 * 
 * Includes a {@link DefaultConsumer} - ideally we would just use the
 * DefaultConsumer as is, but it requires knowledge of the {@link Channel} on
 * construction - whereas the way the {@link RabbitMqAdapter} works, the
 * consumer is required before the channel is created. This implementation
 * allows creation of the consumer, then the internal {@link DefaultConsumer} is
 * created when the channel is set via the provided method
 *
 */
public abstract class SmiConsumer extends DefaultConsumer {

    public SmiConsumer(Channel channel) {
        super(channel);
    }

    /**
     * Handles a message delivery. Needs to be implemented by subclasses.
     * 
     * @see com.rabbitmq.client.Consumer#handleDelivery(java.lang.String,
     *      com.rabbitmq.client.Envelope, com.rabbitmq.client.AMQP.BasicProperties,
     *      byte[])
     */
    public void handleDelivery(String consumerTag, Envelope envelope, AMQP.BasicProperties properties, byte[] body)
            throws IOException {

        MessageHeader header = null;
        Charset enc = StandardCharsets.UTF_8;

        if (properties.getHeaders().size() != 0) {

            if (properties.getContentEncoding() != null)
                enc = Charset.forName(properties.getContentEncoding());

            header = new MessageHeader(properties.getHeaders(), enc);
        }

        handleDeliveryImpl(consumerTag, envelope, properties, body, header);
    }

    public abstract void handleDeliveryImpl(String consumerTag, Envelope envelope, BasicProperties properties,
            byte[] body, MessageHeader header) throws IOException;

    /**
     * Helper method for extracting a particular message type from the JSON
     * 
     * Uses the expectedClass to create a deserializer for the particular message
     * type, then attempts to deserialize it. Throws an exception if:
     * 
     * - the body contents do not match the expected fields - any of the required
     * fields are missing - the body encoding is not supported
     * 
     * @param body
     *            The body of the RabbitMQ message
     * @param expectedClass
     *            The class expected to be in the message
     * @return The parsed message
     * @throws UnsupportedEncodingException
     * @throws JsonSyntaxException
     */
    public <T> T getMessageFromBytes(byte[] body, Class<T> expectedClass)
            throws UnsupportedEncodingException, JsonSyntaxException {

        T message = null;

        final Gson gson = new GsonBuilder().registerTypeAdapter(expectedClass, new JsonDeserializerWithOptions<T>())
                .create();

        JsonObject jObj = new JsonParser().parse(new String(body, "UTF-8")).getAsJsonObject();

        message = gson.fromJson(jObj, expectedClass);

        return message;
    }

    /**
     * Acknowledge the RabbitMQ message
     * 
     * @param deliveryTag
     *            The delivery tag associated with the message
     * @throws IOException
     */
    public void AckMessage(long deliveryTag) throws IOException {
        this.getChannel().basicAck(deliveryTag, false);
    }

    /**
     * NAck the RabbitMQ message
     * 
     * @param deliveryTag
     *            The delivery tag associated with the message
     * @throws IOException
     */
    public void NackMessage(long deliveryTag) throws IOException {
       this.getChannel().basicNack(deliveryTag, false, false);
    }
    
    /**
     * Fire up a consumer
     * @param _options 
     * @throws IOException 
     */
    public void consume(ConsumerOptions _options) throws IOException {
        getChannel().basicConsume(_options.QueueName, _options.AutoAck, this);
    }
}